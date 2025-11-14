using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DotNetARX;
using NetTopologySuite.Geometries;
using TimeIsLife.Helper;
using TimeIsLife.Jig;
using TimeIsLife.Model;
using TimeIsLife.ViewModel;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("F3_ConnectMultiLines")]
        public void F3_ConnectMultiLines()
        {

            // 获取当前文档和数据库的引用
            Document doc = Application.DocumentManager.CurrentDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            Matrix3d ucs2wcs = ed.CurrentUserCoordinateSystem;


            ed.WriteMessage(
                "\n作用：多个块按照最近距离自动连线。" +
                "\n操作方法：框选对象。" +
                "\n注意事项：块不能锁定。"
            );

            var ppr = ed.GetPoint("\n请选择第一个角点：");  //获取的UCS点
            if (ppr.Status != PromptStatus.OK) return;
            Point3d startPointWcs = ppr.Value.TransformBy(ucs2wcs);

            //创建一个临时矩形，用于动态拖拽显示
            db.LoadSysLineType(SystemLinetype.DASHED);

            Polyline rect = new Polyline
            {
                Closed = true,
                Linetype = "DASHED",
                Transparency = new Transparency(128),
                ColorIndex = 31,
                LinetypeScale = 1
            };
            for (int i = 0; i < 4; i++)
                rect.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);

            // 3️⃣ 启动 Jig
            var jig = new UcsSelectJig(startPointWcs, rect);
            if (ed.Drag(jig).Status != PromptStatus.OK)
                return;

            Point3d endPointWcs = jig.EndPointWcs;

            //计算矩形的四个角点（WCS）
            Point3dCollection rectPts = GetRectPointsInWcs(startPointWcs, endPointWcs, ucs2wcs);


            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                //获取块表与模型空间
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                //框选所有块参照
                TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                SelectionFilter selFilter = new SelectionFilter(filter);

                PromptSelectionResult psr = ed.SelectWindowPolygon(rectPts, selFilter); //SelectCrossingPolygon是UCS的点集；
                if (psr.Status != PromptStatus.OK)
                {
                    tr.Commit();
                    return;
                }

                try
                {
                    List<BlockReference> brList = new List<BlockReference>();
                    foreach (ObjectId id in psr.Value.GetObjectIds())
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;

                        LayerTableRecord layer = tr.GetObject(br.LayerId, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null && layer.IsLocked) continue;

                        brList.Add(br);
                    }

                    GeometryFactory geometryFactory = CreateGeometryFactory();
                    var points = GetNtsPointsFromBlockreference(geometryFactory, brList)
                        .Distinct()
                        .ToList();

                    List<LineString> tree = Kruskal.FindMinimumSpanningTree(points, geometryFactory);
                    SetCurrentLayer(db, MyPlugin.CurrentUserData.WireLayerName, 1);
                    const double tolerance = 1e-3;
                    foreach (var line in tree)
                    {
                        var startPoint = new Point3d(line.Coordinates[0].X, line.Coordinates[0].Y, 0);
                        var endPoint = new Point3d(line.Coordinates[1].X, line.Coordinates[1].Y, 0);

                        var br1 = brList.FirstOrDefault(b =>
                            Math.Abs(b.Position.X - startPoint.X) < tolerance &&
                            Math.Abs(b.Position.Y - startPoint.Y) < tolerance);
                        var br2 = brList.FirstOrDefault(b =>
                            Math.Abs(b.Position.X - endPoint.X) < tolerance &&
                            Math.Abs(b.Position.Y - endPoint.Y) < tolerance);

                        Line connectline = GetBlockreferenceConnectline1(br1, br2);

                        ms.AppendEntity(connectline);
                        tr.AddNewlyCreatedDBObject(connectline, true);
                    }
                    tr.Commit();
                }
                catch
                {
                    // ignored
                }
            }
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
            Point3d leftDownUcs = new Point3d(minX, minY, 0);
            Point3d rightDownUcs = new Point3d(maxX, minY, 0);
            Point3d rightUpUcs = new Point3d(maxX, maxY, 0);
            Point3d leftUpUcs = new Point3d(minX, maxY, 0);

            // 转回 WCS（SelectCrossingPolygon 需要 WCS 点）
            Point3dCollection result = new Point3dCollection
            {
                leftDownUcs,
                rightDownUcs,
                rightUpUcs,
                leftUpUcs
            };

            return result;
        }


        public List<Point> GetNtsPointsFromBlockreference(GeometryFactory geometryFactory, List<BlockReference> blockReferences)
        {
            var points = new List<Point>();
            foreach (var blockReference in blockReferences)
            {
                points.Add(geometryFactory.CreatePoint(new Coordinate(blockReference.Position.X, blockReference.Position.Y)));
            }
            return points;
        }


        private Point3dCollection GetPoint3DCollection(Point3d up1, Point3d up2, Matrix3d matrix3D)
        {
            //    Point3d p1 = up1.TransformBy(matrix3D.Inverse());
            //    Point3d p2 = up2.TransformBy(matrix3D.Inverse());
            Point3dCollection point3DCollection = new Point3dCollection();
            if (up1.X < up2.X && up1.Y < up2.Y)
            {
                var leftDownPoint = up1;
                var leftUpPoint = new Point3d(up1.X, up2.Y, 0);
                var rightUpPoint = up2;
                var rightDownPoint = new Point3d(up2.X, up1.Y, 0);
                point3DCollection = GetPoint3DCollection(point3DCollection, leftDownPoint, rightDownPoint, rightUpPoint, leftUpPoint, matrix3D);
            }
            else if (up1.X < up2.X && up1.Y > up2.Y)
            {
                var leftDownPoint = new Point3d(up1.X, up2.Y, 0);
                var leftUpPoint = up1;
                var rightUpPoint = new Point3d(up2.X, up1.Y, 0);
                var rightDownPoint = up2;
                point3DCollection = GetPoint3DCollection(point3DCollection, leftDownPoint, rightDownPoint, rightUpPoint, leftUpPoint, matrix3D);
            }
            else if (up1.X > up2.X && up1.Y > up2.Y)
            {
                var leftDownPoint = up2;
                var leftUpPoint = new Point3d(up2.X, up1.Y, 0);
                var rightUpPoint = up1;
                var rightDownPoint = new Point3d(up1.X, up2.Y, 0);
                point3DCollection = GetPoint3DCollection(point3DCollection, leftDownPoint, rightDownPoint, rightUpPoint, leftUpPoint, matrix3D);
            }
            else
            {
                var leftDownPoint = new Point3d(up2.X, up1.Y, 0);
                var leftUpPoint = up2;
                var rightUpPoint = new Point3d(up1.X, up2.Y, 0);
                var rightDownPoint = up1;
                point3DCollection = GetPoint3DCollection(point3DCollection, leftDownPoint, rightDownPoint, rightUpPoint, leftUpPoint, matrix3D);
            }
            return point3DCollection;
        }

        private Point3dCollection GetPoint3DCollection(Point3dCollection point3DCollection, Point3d leftDownPoint, Point3d rightDownPoint, Point3d rightUpPoint, Point3d leftUpPoint, Matrix3d matrix3D)
        {
            //    point3DCollection.Add(leftDownPoint.TransformBy(matrix3D.Inverse()));
            //    point3DCollection.Add(rightDownPoint.TransformBy(matrix3D.Inverse()));
            //    point3DCollection.Add(rightUpPoint.TransformBy(matrix3D.Inverse()));
            //    point3DCollection.Add(leftUpPoint.TransformBy(matrix3D.Inverse()));

            point3DCollection.Add(leftDownPoint);
            point3DCollection.Add(rightDownPoint);
            point3DCollection.Add(rightUpPoint);
            point3DCollection.Add(leftUpPoint);

            return point3DCollection;
        }

        private Line GetBlockreferenceConnectline1(BlockReference firstBlock, BlockReference secondBlock)
        {
            Database database = firstBlock.Database;
            Line connectline = new Line();

            // 事务用于访问数据库对象
            using Transaction transaction = database.TransactionManager.StartTransaction();
            // 获取第一个块的连接点
            Point3dCollection firstBlockPoints = firstBlock.GetConnectionPoints();
            // 获取第二个块的连接点
            Point3dCollection secondBlockPoints = secondBlock.GetConnectionPoints();

            // 如果两个块中至少有一个没有连接点，则不继续执行
            if (firstBlockPoints.Count == 0 || secondBlockPoints.Count == 0)
            {
                return connectline;
            }

            // 计算最近的点对
            double minDistance = double.MaxValue;
            Point3d closestPointFromFirstBlock = Point3d.Origin;
            Point3d closestPointFromSecondBlock = Point3d.Origin;

            foreach (Point3d pointFromFirstBlock in firstBlockPoints)
            {
                foreach (Point3d pointFromSecondBlock in secondBlockPoints)
                {
                    double distance = pointFromFirstBlock.DistanceTo(pointFromSecondBlock);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPointFromFirstBlock = pointFromFirstBlock;
                        closestPointFromSecondBlock = pointFromSecondBlock;
                    }
                }
            }

            // 创建一条连接这两个最近点的线
            if (minDistance < double.MaxValue)
            {
                connectline = new Line(closestPointFromFirstBlock, closestPointFromSecondBlock);
            }

            return connectline;
        }

    }
}
