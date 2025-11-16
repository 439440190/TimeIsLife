using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using DotNetARX;

using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NetTopologySuite;

using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using TimeIsLife.Jig;
using TimeIsLife.Model;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Point = NetTopologySuite.Geometries.Point;
using TimeIsLife.Helper;

// 该行不是必需的，但是可以提高加载性能
[assembly: CommandClass(typeof(TimeIsLife.CADCommand.TilCommand))]

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        /// <summary>
        /// 缓存的 GeometryFactory 实例，用于提高性能
        /// </summary>
        private static GeometryFactory _cachedGeometryFactory;
        private static readonly object _geometryFactoryLock = new object();

        /// <summary>
        /// 获取NTS指定精度和标准坐标系的GeometryFactory实例（带缓存）
        /// </summary>
        /// <returns>GeometryFactory实例</returns>
        private GeometryFactory CreateGeometryFactory()
        {
            // 如果缓存中已有实例，直接返回
            if (_cachedGeometryFactory != null)
                return _cachedGeometryFactory;

            // 线程安全地创建单例
            lock (_geometryFactoryLock)
            {
                if (_cachedGeometryFactory != null)
                    return _cachedGeometryFactory;

                var precisionModel = new PrecisionModel(1000d);
                NtsGeometryServices.Instance = new NetTopologySuite.NtsGeometryServices(
                    NetTopologySuite.Geometries.Implementation.CoordinateArraySequenceFactory.Instance,
                    precisionModel,
                    4326
                );

                _cachedGeometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(precisionModel);
            }

            return _cachedGeometryFactory;
        }

        /// <summary>
        /// 创建连接两块参照的最短线段
        /// </summary>
        /// <param name="br1">第一个块参照</param>
        /// <param name="br2">第二个块参照</param>
        /// <returns>连接两块参照的最短线段，如果无法连接则返回 null</returns>
        private Line BuildConnectLine(BlockReference br1, BlockReference br2)
        {
            if (br1 == null || br2 == null)
                return null;

            var db = br1.Database;

            using var tr = db.TransactionManager.StartTransaction();

            // 获取两个块参照的连接点
            var pts1 = br1.GetConnectionPoints();
            var pts2 = br2.GetConnectionPoints();

            // 如果任意块参照没有连接点，则返回 null
            if (pts1.Count == 0 || pts2.Count == 0)
                return null;

            // 使用 Linq 查找最近的点对（更高效的实现）
            double minDistance = double.MaxValue;
            Point3d p1 = default, p2 = default;

            foreach (Point3d a in pts1)
            {
                foreach (Point3d b in pts2)
                {
                    double distance = a.DistanceTo(b);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        p1 = a;
                        p2 = b;
                    }
                }
            }

            // 如果找到有效的最短距离，则创建并返回线段
            return minDistance < double.MaxValue ? new Line(p1, p2) : null;
        }

        /// <summary>
        /// 根据两个 WCS 点和当前 UCS->WCS 矩阵，返回矩形四个角的 WCS 点集合（用于 SelectCrossingPolygon）
        /// 顺序：左下、右下、右上、左上（闭合顺序）
        /// </summary>
        private Point3dCollection GetRectPointsInWcs(Point3d p1Wcs, Point3d p2Wcs, Matrix3d ucs2wcs)
        {
            // 把两个 WCS 点变回 UCS，便于按 UCS XY 平面构造矩形
            Point3d p1Ucs = p1Wcs.TransformBy(ucs2wcs.Inverse());
            Point3d p2Ucs = p2Wcs.TransformBy(ucs2wcs.Inverse());

            double minX = Math.Min(p1Ucs.X, p2Ucs.X);
            double maxX = Math.Max(p1Ucs.X, p2Ucs.X);
            double minY = Math.Min(p1Ucs.Y, p2Ucs.Y);
            double maxY = Math.Max(p1Ucs.Y, p2Ucs.Y);

            // 在 UCS 平面上构造四角（Z=0）
            Point3dCollection result = new Point3dCollection
            {
                new Point3d(minX, minY, 0),  // 左下
                new Point3d(maxX, minY, 0),  // 右下
                new Point3d(maxX, maxY, 0),  // 右上
                new Point3d(minX, maxY, 0)   // 左上
            };
            return result;
        }
    }
}
