﻿using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

using DotNetARX;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace TimeIsLife.Helper
{
    internal static class PolylineHelper
    {
        public static Point3dCollection GetPoint3DCollection(this Polyline polyline)
        {
            Point3dCollection points = new Point3dCollection();

            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                points.Add(polyline.GetPoint3dAt(i));
            }
            return points;
        }

        public static Point2dCollection GetPoint2DCollection(this Polyline polyline)
        {
            Point2dCollection points = new Point2dCollection();

            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                points.Add(new Point2d(polyline.GetPoint3dAt(i).X, polyline.GetPoint3dAt(i).Y));
            }
            return points;
        }

        /// <summary>
        /// 判断多边形1是否在多边形2的内部，true为在内部，false为不在内部
        /// </summary>
        /// <param name="roomPolyline">多边形1</param>
        /// <param name="slabPolyline">多边形2</param>
        /// <returns></returns>
        public static bool IsPolylineInPolyline(this Polyline Polyline1, Polyline Polyline2)
        {
            bool bo = true;
            for (int i = 0; i < Polyline1.NumberOfVertices; i++)
            {
                if (!Polyline1.GetPoint2dAt(i).IsInPolygon2(Polyline2.GetPoint2DCollection().ToArray().ToList()))
                {
                    bo = false;
                }
            }
            return bo;
        }
    }
}
