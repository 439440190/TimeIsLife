using Accord.MachineLearning;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DotNetARX;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NetTopologySuite.Triangulate;
using NetTopologySuite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using TimeIsLife.Helper;
using Autodesk.AutoCAD.ApplicationServices;
using TimeIsLife.Model;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Distance;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("FF_FireAlarmCeiling")]
        public void FF_FireAlarmCeiling()
        {
            // 初始化
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            //初始化NTS的GeometryFactory
            PrecisionModel precisionModel = new PrecisionModel(1000d);
            NtsGeometryServices.Instance = new NtsGeometryServices(
                NetTopologySuite.Geometries.Implementation.CoordinateArraySequenceFactory.Instance,
                precisionModel,
                4326
            );
            GeometryFactory geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(precisionModel);

            // 提示用户选择保护半径
            PromptIntegerOptions promptIntegerOptions = new PromptIntegerOptions("\n请选择保护半径 (3600, 4400, 5800, 6700): ")
            {
                AllowNone = false,
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = 5800,
                UseDefaultValue = true
            };

            // 提示用户选择保护半径值
            PromptIntegerResult promptIntegerResult = editor.GetInteger(promptIntegerOptions);

            // 检查用户输入是否有效
            if (promptIntegerResult.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\n无效的输入！");
                return;
            }

            // 获取用户选择的保护半径值
            int radius = promptIntegerResult.Value;
            if (radius != 3600 && radius != 4400 && radius != 5800 && radius != 6700)
            {
                editor.WriteMessage("\n选择的保护半径值无效！");
                return;
            }

            // 事务开始
            using Transaction transaction = database.TransactionManager.StartTransaction();
            try
            {
                // 获取块表和模型空间
                BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 设置选择集选项和过滤器
                PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions { RejectObjectsOnLockedLayers = true };
                TypedValueList typedValues = new TypedValueList { typeof(Polyline) };
                SelectionFilter selectionFilter = new SelectionFilter(typedValues);

                // 获取选择集
                SelectionSet selectionSet = editor.GetSelectionSet(SelectString.GetSelection, promptSelectionOptions, selectionFilter, null);

                if (selectionSet == null)
                {
                    editor.WriteMessage("\n未选择任何对象。");
                    transaction.Abort();
                    return;
                }

                // 获取选择集中的多段线生成的Polygon
                List<Polygon> polygons = selectionSet.GetObjectIds()
                    .Select(id => transaction.GetObject(id, OpenMode.ForRead) as Polyline)
                    .Where(polyline => polyline != null)
                    .Select(polyline => polyline.ToPolygon(geometryFactory))
                    .Where(polygon => polygon != null && !polygon.IsEmpty)
                    .Select(polygon =>
                    {
                        if (!polygon.IsValid)
                        {
                            var fixedGeometry = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(polygon);
                            if (fixedGeometry is Polygon fixedPolygon)
                                return fixedPolygon;
                            if (fixedGeometry is MultiPolygon multiPolygon && multiPolygon.NumGeometries == 1)
                                return (Polygon)multiPolygon.GetGeometryN(0);
                        }
                        return polygon;
                    })
                    .ToList();

                List<Coordinate> coordinates = new List<Coordinate>();

                //foreach (var polygon in polygons)
                //{
                //    if (IsProtected(polygon, radius))
                //    {
                //        coordinates.Add(polygon.Centroid.Coordinate);
                //    }
                //    else
                //    {
                //        int n = 2;
                //        bool isAllProtected = false;

                //        while (!isAllProtected && n <= 100)
                //        {
                //            List<Geometry> splitGeometries = SplitPolygonOptimized(geometryFactory, polygon, n, 500);
                //            isAllProtected = splitGeometries.All(item => IsProtected(item, radius));

                //            if (isAllProtected)
                //            {
                //                coordinates.AddRange(splitGeometries.Select(item => item.Centroid.Coordinate));
                //            }
                //            else
                //            {
                //                n++;
                //            }
                //        }

                //        if (n > 100)
                //        {
                //            editor.WriteMessage("_.ALERT", "点位布置超过100个，请减少区域面积！");
                //        }
                //    }
                //}

                //foreach (var polygon in polygons)
                //{
                //    if (IsProtected(polygon, radius))
                //    {
                //        coordinates.Add(polygon.Centroid.Coordinate);
                //    }
                //    else
                //    {
                //        List<Geometry> splitGeometries = SplitPolygonWithGrid(geometryFactory, polygon, 2*radius / Math.Sqrt(2));
                //        coordinates.AddRange(splitGeometries.Select(item => item.Centroid.Coordinate));
                //    }
                //}
                CoulombForceSimulator simulator = new CoulombForceSimulator();

                foreach (var polygon in polygons)
                {
                    if (IsProtected(polygon, radius))
                    {
                        coordinates.Add(polygon.Centroid.Coordinate);
                    }
                    else
                    {
                        List<Coordinate> coordinates1 = MinimumCircleCover(polygon, radius);
                        coordinates.AddRange(simulator.DistributePoints(polygon, 4));
                    }
                }

                if (coordinates.Count == 0)
                {
                    editor.WriteMessage("\n未布置探测器！");
                    transaction.Abort();
                    return;
                }



                // 提前加载并缓存模块ID
                var smokeDetectorId = LoadBlockIntoDatabase(MyPlugin.CurrentUserData.FireAlarmEquipments.FirstOrDefault(f =>
                    f.EquipmentType == FireAlarmEquipmentType.Fa08)!.BlockPath);
                var temperatureDetectorId = LoadBlockIntoDatabase(MyPlugin.CurrentUserData.FireAlarmEquipments.FirstOrDefault(f =>
                    f.EquipmentType == FireAlarmEquipmentType.Fa09)!.BlockPath);

                ObjectId blockReferenceId = (radius == 3600 || radius == 4400) ? temperatureDetectorId : smokeDetectorId;

                // 设置当前图层并插入块参照
                SetCurrentLayer(database, $"E-EQUIP", 4);
                foreach (var coordinate in coordinates)
                {
                    BlockReference blockReference = new BlockReference(new Point3d(coordinate.X, coordinate.Y, 0), blockReferenceId)
                    {
                        ScaleFactors = new Scale3d(100)
                    };
                    database.AddToModelSpace(blockReference);
                }
            }
            catch
            {
                transaction.Abort();
                throw;
            }
            transaction.Commit();
        }

        private List<Geometry> SplitPolygonWithGrid(GeometryFactory geometryFactory, Geometry geometry, double gridSize)
        {
            // 计算几何图形的外包围盒（Envelope）
            Envelope envelope = geometry.EnvelopeInternal;

            // 计算最大和最小点的坐标
            double minX = envelope.MinX;
            double minY = envelope.MinY;
            double maxX = envelope.MaxX;
            double maxY = envelope.MaxY;

            // 计算分割点的数量
            int numX = (int)Math.Ceiling((maxX - minX) / gridSize);
            int numY = (int)Math.Ceiling((maxY - minY) / gridSize);

            // 创建分割的多边形列表
            List<Geometry> gridPolygons = new List<Geometry>();

            // 创建网格并判断网格是否与多边形相交
            for (int i = 0; i < numX; i++)
            {
                for (int j = 0; j < numY; j++)
                {
                    double x1 = minX + i * gridSize;
                    double y1 = minY + j * gridSize;
                    double x2 = x1 + gridSize;
                    double y2 = y1 + gridSize;

                    // 创建网格方块
                    Coordinate[] coordinates = new Coordinate[]
                    {
                new Coordinate(x1, y1),
                new Coordinate(x2, y1),
                new Coordinate(x2, y2),
                new Coordinate(x1, y2),
                new Coordinate(x1, y1)
                    };
                    Polygon gridPolygon = geometryFactory.CreatePolygon(coordinates);

                    // 获取网格方块和原始多边形的交集
                    Geometry intersection = gridPolygon.Intersection(geometry);
                    if (!intersection.IsEmpty)
                    {
                        gridPolygons.Add(intersection);
                    }
                }
            }

            return gridPolygons;
        }

        private List<Geometry> SplitPolygonUsingGrid(GeometryFactory geometryFactory, Geometry geometry, double interval)
        {
            // 计算几何图形的外包围盒（Envelope）
            Envelope envelope = geometry.EnvelopeInternal;

            // 计算最大和最小点的坐标
            double minX = envelope.MinX;
            double minY = envelope.MinY;
            double maxX = envelope.MaxX;
            double maxY = envelope.MaxY;

            List<Geometry> gridCells = new List<Geometry>();

            // 构建网格点并判断点是否在多边形内部
            for (double x = minX; x < maxX; x += interval)
            {
                for (double y = minY; y < maxY; y += interval)
                {
                    // 创建矩形单元
                    Coordinate[] coordinates = new Coordinate[]
                    {
                new Coordinate(x, y),
                new Coordinate(x + interval, y),
                new Coordinate(x + interval, y + interval),
                new Coordinate(x, y + interval),
                new Coordinate(x, y)
                    };

                    LinearRing ring = geometryFactory.CreateLinearRing(coordinates);
                    Polygon cell = geometryFactory.CreatePolygon(ring);

                    // 检查网格单元是否与几何图形相交
                    if (geometry.Intersects(cell))
                    {
                        Geometry intersection = geometry.Intersection(cell);
                        if (!intersection.IsEmpty)
                        {
                            gridCells.Add(intersection);
                        }
                    }
                }
            }

            return gridCells;
        }


        //完全覆盖
        public List<Coordinate> MinimumCircleCover(Polygon polygon, double radius)
        {
            List<Coordinate> circles = new List<Coordinate>();

            Envelope envelope = polygon.EnvelopeInternal;
            double minX = envelope.MinX;
            double minY = envelope.MinY;
            double maxX = envelope.MaxX;
            double maxY = envelope.MaxY;

            double horizontalDistance = Math.Sqrt(3) * radius;
            double verticalDistance = 1.5 * radius;

            for (double y = minY - radius; y <= maxY + radius; y += verticalDistance)
            {
                for (double x = minX - radius; x <= maxX + radius; x += horizontalDistance)
                {
                    double adjustedX = x;
                    if (((int)((y - minY) / verticalDistance)) % 2 == 1)
                    {
                        adjustedX += horizontalDistance / 2;
                    }

                    Point circleCenter = new Point(adjustedX, y);
                    if (polygon.Contains(circleCenter) || polygon.Intersects(circleCenter.Buffer(radius)))
                    {
                        circles.Add(new Coordinate(circleCenter.X, circleCenter.Y));
                    }
                }
            }

            return circles;
        }
    }

    public class CoulombForceSimulator
    {
        private const double Tolerance = 0.01;
        private const double RepulsionConstant = 1.0;
        private const double TimeStep = 0.01;

        public List<Coordinate> DistributePoints(Polygon polygon, int numberOfPoints)
        {
            List<Coordinate> points = GenerateRandomPointsInPolygon(polygon, numberOfPoints);

            bool isStable;
            do
            {
                isStable = UpdatePoints(points, polygon);
            } while (!isStable);

            return points;
        }

        private List<Coordinate> GenerateRandomPointsInPolygon(Polygon polygon, int numberOfPoints)
        {
            Random random = new Random();
            List<Coordinate> points = new List<Coordinate>();
            Envelope envelope = polygon.EnvelopeInternal;

            while (points.Count < numberOfPoints)
            {
                double x = envelope.MinX + random.NextDouble() * (envelope.MaxX - envelope.MinX);
                double y = envelope.MinY + random.NextDouble() * (envelope.MaxY - envelope.MinY);
                Coordinate candidate = new Coordinate(x, y);
                if (polygon.Contains(new Point(candidate)))
                {
                    points.Add(candidate);
                }
            }

            return points;
        }

        private bool UpdatePoints(List<Coordinate> points, Polygon polygon)
        {
            bool isStable = true;
            List<Coordinate> newPoints = new List<Coordinate>(points.Count);

            double avgDistance = CalculateAverageDistance(points);
            double targetDistanceToEdge = avgDistance / 2;

            for (int i = 0; i < points.Count; i++)
            {
                Coordinate point = points[i];
                Coordinate newPoint = new Coordinate(point.X, point.Y);

                Vector totalForce = new Vector(0, 0);

                for (int j = 0; j < points.Count; j++)
                {
                    if (i == j) continue;

                    Coordinate otherPoint = points[j];
                    Vector force = CalculateRepulsionForce(point, otherPoint);
                    totalForce += force;
                }

                // Calculate force to move point closer to target distance from edge
                Coordinate closestPointOnEdge = DistanceOp.NearestPoints(polygon, new Point(point))[0];
                double distanceToEdge = point.Distance(closestPointOnEdge);
                double edgeForceMagnitude = RepulsionConstant * (distanceToEdge - targetDistanceToEdge);
                Vector edgeForce = new Vector(
                    edgeForceMagnitude * (closestPointOnEdge.X - point.X) / distanceToEdge,
                    edgeForceMagnitude * (closestPointOnEdge.Y - point.Y) / distanceToEdge
                );

                totalForce += edgeForce;

                newPoint.X += totalForce.X * TimeStep;
                newPoint.Y += totalForce.Y * TimeStep;

                if (!polygon.Contains(new Point(newPoint)))
                {
                    newPoint = point;
                }

                if (Math.Abs(newPoint.X - point.X) > Tolerance || Math.Abs(newPoint.Y - point.Y) > Tolerance)
                {
                    isStable = false;
                }

                newPoints.Add(newPoint);
            }

            for (int i = 0; i < points.Count; i++)
            {
                points[i].X = newPoints[i].X;
                points[i].Y = newPoints[i].Y;
            }

            return isStable;
        }

        private double CalculateAverageDistance(List<Coordinate> points)
        {
            double totalDistance = 0.0;
            int count = 0;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    totalDistance += points[i].Distance(points[j]);
                    count++;
                }
            }

            return totalDistance / count;
        }

        private Vector CalculateRepulsionForce(Coordinate point1, Coordinate point2)
        {
            double dx = point1.X - point2.X;
            double dy = point1.Y - point2.Y;
            double distanceSquared = dx * dx + dy * dy;
            double distance = Math.Sqrt(distanceSquared);

            if (distance < Tolerance)
            {
                distance = Tolerance;
            }

            double forceMagnitude = RepulsionConstant / distanceSquared;
            double fx = forceMagnitude * dx / distance;
            double fy = forceMagnitude * dy / distance;

            return new Vector(fx, fy);
        }

        public struct Vector
        {
            public double X;
            public double Y;

            public Vector(double x, double y)
            {
                X = x;
                Y = y;
            }

            public static Vector operator +(Vector v1, Vector v2)
            {
                return new Vector(v1.X + v2.X, v1.Y + v2.Y);
            }

            public static Vector operator -(Vector v1, Vector v2)
            {
                return new Vector(v1.X - v2.X, v1.Y - v2.Y);
            }
        }
    }

}
