using Accord.MachineLearning;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows.ToolPalette;

using Dapper;


using DotNetARX;
using NetTopologySuite;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Triangulate;

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;

using TimeIsLife.Helper;
using TimeIsLife.NTSHelper;
using TimeIsLife.ViewModel;
using TimeIsLife.Model;

using static TimeIsLife.CADCommand.FireAlarmCommand1;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryCollection = NetTopologySuite.Geometries.GeometryCollection;
using Accord.Math;
using Accord;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Windows.Data;
using Autodesk.AutoCAD.Windows.Features.PointCloud.PointCloudColorMapping;
using static System.Net.Mime.MediaTypeNames;
using System.Collections;
using System.Windows.Interop;
using System.Windows;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using TimeIsLife.View;
using Point = NetTopologySuite.Geometries.Point;
using TimeIsLife.Jig;
using MessageBox = System.Windows.Forms.MessageBox;

using Database = Autodesk.AutoCAD.DatabaseServices.Database;
using System.Windows.Shapes;
using Polygon = NetTopologySuite.Geometries.Polygon;
using Path = System.IO.Path;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Utilities;
using System.Windows.Media.Media3D;
using System.Numerics;
using NetTopologySuite.Precision;
using Accord.Math.Geometry;
using NetTopologySuite.Operation.Distance;
using System.Drawing;
using NetTopologySuite.Geometries.Utilities;
using Accord.Collections;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("F7_LayoutEquipment")]
        public void F7_LayoutEquipment()
        {

            F7_Window.Instance.ShowDialog();
            if (F7_WindowViewModel.Instance.Result != true) return;

            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            List<Beam> beams;
            List<Floor> floors;
            List<Slab> slabs;
            List<Wall> walls;

            //初始化NTS的GeometryFactory
            PrecisionModel precisionModel = new PrecisionModel(1000d);
            GeometryPrecisionReducer precisionReducer = new GeometryPrecisionReducer(precisionModel);
            NtsGeometryServices.Instance = new NtsGeometryServices
                (
                NetTopologySuite.Geometries.Implementation.CoordinateArraySequenceFactory.Instance,
                precisionModel,
                4326
                );
            GeometryFactory geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(precisionModel);

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                try
                {

                    //获取块表
                    BlockTable blockTable = transaction.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    //获取模型空间
                    BlockTableRecord modelSpace = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    //获取图纸空间
                    BlockTableRecord paperSpace = transaction.GetObject(blockTable[BlockTableRecord.PaperSpace], OpenMode.ForRead) as BlockTableRecord;

                    #region YDB数据查询
                    // YDB数据查询
                    if (string.IsNullOrWhiteSpace(F7_WindowViewModel.Instance.YdbFileName))
                    {
                        MessageBox.Show(@"请选择结构数据库");
                        return;
                    }
                    var ydbHelper = new SQLiteHelper($"Data Source={F7_WindowViewModel.Instance.YdbFileName}");
                    using (var ydbConn = ydbHelper.GetConnection())
                    {
                        string sqlFloor = "SELECT f.ID,f.Name,f.LevelB,f.Height FROM tblFloor AS f WHERE f.Height != 0";
                        floors = ydbConn.Query<Floor>(sqlFloor).ToList();

                        string sqlBeam = "SELECT b.ID,f.ID,f.LevelB,f.Height,bs.ID,bs.kind,bs.ShapeVal,bs.ShapeVal1,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                                "FROM tblBeamSeg AS b " +
                                "INNER JOIN tblFloor AS f on f.StdFlrID = b.StdFlrID " +
                                "INNER JOIN tblBeamSect AS bs on bs.ID = b.SectID " +
                                "INNER JOIN tblGrid AS g on g.ID = b.GridId " +
                                "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                                "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID";
                        Func<Beam, Floor, BeamSect, Grid, Joint, Joint, Beam> mappingBeam =
                            (beam, floor, beamSect, grid, j1, j2) =>
                            {
                                grid.Joint1 = j1;
                                grid.Joint2 = j2;
                                beam.Grid = grid;
                                beam.Floor = floor;
                                beam.BeamSect = beamSect;
                                return beam;
                            };
                        beams = ydbConn.Query(sqlBeam, mappingBeam).ToList();

                        string sqlSlab = "SELECT s.ID,s.GridsID,s.VertexX,s.VertexY,s.VertexZ,s.Thickness,f.ID,f.LevelB,f.Height FROM tblSlab  AS s INNER JOIN tblFloor AS f on f.StdFlrID = s.StdFlrID";
                        Func<Slab, Floor, Slab> mappingSlab =
                            (slab, floor) =>
                            {
                                slab.Floor = floor;
                                return slab;
                            };
                        slabs = ydbConn.Query<Slab, Floor, Slab>(sqlSlab, mappingSlab).ToList();

                        string sqlWall = "SELECT w.ID,f.ID,f.LevelB,f.Height,ws.ID,ws.kind,ws.B,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                                "FROM tblWallSeg AS w " +
                                "INNER JOIN tblFloor AS f on f.StdFlrID = w.StdFlrID " +
                                "INNER JOIN tblWallSect AS ws on ws.ID = w.SectID " +
                                "INNER JOIN tblGrid AS g on g.ID = w.GridId " +
                                "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                                "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID";
                        Func<Wall, Floor, WallSect, Grid, Joint, Joint, Wall> mappingWall =
                            (wall, floor, wallSect, grid, j1, j2) =>
                            {
                                grid.Joint1 = j1;
                                grid.Joint2 = j2;
                                wall.Grid = grid;
                                wall.Floor = floor;
                                wall.WallSect = wallSect;
                                return wall;
                            };
                        walls = ydbConn.Query(sqlWall, mappingWall).ToList();
                    }
                    #endregion



                    var smokeDetectorId = LoadBlockIntoDatabase(MyPlugin.CurrentUserData.FireAlarmEquipments.FirstOrDefault(f =>
                        f.EquipmentType == FireAlarmEquipmentType.Fa08)!.BlockPath);
                    var temperatureDetectorId = LoadBlockIntoDatabase(MyPlugin.CurrentUserData.FireAlarmEquipments.FirstOrDefault(f =>
                        f.EquipmentType == FireAlarmEquipmentType.Fa09)!.BlockPath);

                    #region 根据房间边界及板轮廓生成烟感

                    foreach (var curFloorArea in F7_WindowViewModel.Instance.Areas)
                    {
                        List<Model.Area> curFireAreas = new List<Model.Area>();
                        List<Model.Area> curRoomAreas = new List<Model.Area>();
                        //过滤器
                        TypedValueList typedValues = new TypedValueList
                        {
                            typeof(Polyline)
                        };
                        SelectionFilter selectionFilter1 = new SelectionFilter(typedValues);
                        Point3dCollection point3DCollection1 = curFloorArea.Point3dCollection.TransformBy(ucsToWcsMatrix3d.Inverse());
                        SelectionSet selectionSet = editor.GetSelectionSet(SelectString.SelectCrossingPolygon, null, selectionFilter1, point3DCollection1);

                        if (selectionSet.Count == 0) continue;

                        //获取当前楼层所有的防火分区和房间
                        foreach (var id in selectionSet.GetObjectIds())
                        {
                            Polyline polyline = transaction.GetObject(id, OpenMode.ForRead) as Polyline;
                            if (polyline == null) continue;
                            if (polyline.Layer == F7_WindowViewModel.Instance.FireAreaLayerName || polyline.Layer == F7_WindowViewModel.Instance.RoomAreaLayerName)
                            {
                                TypedValueList dbTextTypedValues = new TypedValueList
                                {
                                    { DxfCode.LayerName, polyline.Layer },
                                    typeof(DBText)
                                };
                                SelectionFilter selectionFilter2 = new SelectionFilter(dbTextTypedValues);
                                SelectionSet dbTextSelectionSet = editor.GetSelectionSet(SelectString.SelectCrossingPolygon, null, selectionFilter2, polyline.GetPoint3dCollection(ucsToWcsMatrix3d.Inverse()));
                                List<string> notes = new List<string>();
                                if (dbTextSelectionSet == null || dbTextSelectionSet.Count == 0) continue;
                                foreach (var textId in dbTextSelectionSet.GetObjectIds())
                                {
                                    DBText dBText = transaction.GetObject(textId, OpenMode.ForRead) as DBText;
                                    if (dBText == null) continue;
                                    notes.Add(dBText.TextString);
                                }

                                Model.Area newArea = null;
                                if (polyline.Layer == F7_WindowViewModel.Instance.FireAreaLayerName)
                                {
                                    newArea = new Model.Area
                                    {
                                        Kind = 1,
                                        Note = string.Join(",", notes.ToArray()),
                                        VertexX = polyline.GetXValues(),
                                        VertexY = polyline.GetYValues(),
                                        VertexZ = polyline.GetZValues()
                                    };
                                }
                                else if (polyline.Layer == F7_WindowViewModel.Instance.RoomAreaLayerName)
                                {
                                    newArea = new Model.Area
                                    {
                                        Kind = 2,
                                        Note = string.Join(",", notes.ToArray()),
                                        VertexX = polyline.GetXValues(),
                                        VertexY = polyline.GetYValues(),
                                        VertexZ = polyline.GetZValues()
                                    };
                                }
                                Polygon roomPolygon = geometryFactory.CreatePolygon(GetAreaCoordinates(newArea));
                                var fixedGeometry = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(roomPolygon);

                                if (fixedGeometry is Polygon)
                                {
                                    // 如果修复后的结果是 Polygon，直接使用
                                    Polygon fixedPolygon = (Polygon)fixedGeometry;
                                    // 使用 fixedPolygon 进行后续操作
                                }
                                else if (fixedGeometry is MultiPolygon multiPolygon && multiPolygon.NumGeometries == 1)
                                {
                                    // 如果修复后的结果是 MultiPolygon 且只包含一个 Polygon，取出这个 Polygon
                                    Polygon singlePolygon = (Polygon)multiPolygon.GetGeometryN(0);
                                    // 使用 singlePolygon 进行后续操作
                                }
                                else
                                {
                                    // 如果修复后的结果是包含多个 Polygon 的 MultiPolygon，或者其他情况
                                    // 根据需要处理或抛出异常
                                    polyline.Highlight();
                                    transaction.Abort();
                                    return;
                                }
                                if (newArea != null)
                                {
                                    curRoomAreas.Add(newArea);
                                }
                            }
                        }

                        SetCurrentLayer(database, F7_WindowViewModel.Instance.EquipmentLayerName, 4);
                        List<Beam> curFloorBeams = beams.Where(beam => beam.Floor.LevelB == curFloorArea.Level).ToList();
                        List<Slab> curFloorSlabs = slabs.Where(slab => slab.Floor.LevelB == curFloorArea.Level).ToList();
                        List<Wall> curFloorWalls = walls.Where(wall => wall.Floor.LevelB == curFloorArea.Level).ToList();
                        List<BlockReference> blockReferences = new List<BlockReference>();
                        Vector3d vector3D = Point3d.Origin.GetVectorTo(curFloorArea.BasePoint) + F7_WindowViewModel.Instance.BaseVector;

                        foreach (var roomArea in curRoomAreas)
                        {
                            //判断房间类型
                            string areaNote = roomArea.Note;
                            HashSet<string> resultSet = GetRoomType(areaNote);

                            //没有标记的区域跳过
                            if (resultSet.Count == 0) continue;

                            //根据区域标记匹配探测器参数
                            foreach (string result in resultSet)
                            {
                                DetectorInfo detectorInfo = new DetectorInfo();
                                switch (result)
                                {
                                    case "A1H2":
                                        detectorInfo.Radius = 6700;
                                        detectorInfo.Area1 = 16;
                                        detectorInfo.Area2 = 24;
                                        detectorInfo.Area3 = 32;
                                        detectorInfo.Area4 = 48;
                                        break;
                                    case "A1H3":
                                        detectorInfo.Radius = 5800;
                                        detectorInfo.Area1 = 12;
                                        detectorInfo.Area2 = 18;
                                        detectorInfo.Area3 = 24;
                                        detectorInfo.Area4 = 36;
                                        break;
                                    case "A2H2":
                                        detectorInfo.Radius = 4400;
                                        detectorInfo.Area1 = 6;
                                        detectorInfo.Area2 = 9;
                                        detectorInfo.Area3 = 12;
                                        detectorInfo.Area4 = 18;
                                        break;
                                    case "A2H3":
                                        detectorInfo.Radius = 3600;
                                        detectorInfo.Area1 = 4;
                                        detectorInfo.Area2 = 6;
                                        detectorInfo.Area3 = 8;
                                        detectorInfo.Area4 = 12;
                                        break;
                                }

                                Polygon roomPolygon = geometryFactory.CreatePolygon(GetAreaCoordinates(roomArea));
                                Dictionary<Geometry, double> intersectionPolygonDictionary = new Dictionary<Geometry, double>();

                                //获取房间区域的梁窝
                                foreach (var slab in curFloorSlabs)
                                {
                                    //楼板为空，跳过
                                    if (string.IsNullOrEmpty(slab.VertexX) || string.IsNullOrEmpty(slab.VertexY) || string.IsNullOrEmpty(slab.VertexZ)) continue;
                                    slab.TranslateVertices(vector3D);
                                    Polygon slabPolygon = geometryFactory.CreatePolygon(GetSlabCoordinates(slab));

                                    //检查 roomPolygon 是否与 slabPolygon 相交。
                                    //如果 roomPolygon 或 slabPolygon 无效，则使用 NetTopologySuite GeometryFixer 进行修复。
                                    //计算两个多边形的相交几何体。
                                    //如果相交几何体是非空多边形，并且板的厚度在指定范围内，则将相交几何体和对应的厚度添加到一个字典（IntersectionPolygonDictionary）中。
                                    if (roomPolygon.Intersects(slabPolygon))
                                    {
                                        if (!roomPolygon.IsValid)
                                        {
                                            var fixedGeometry = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(roomPolygon);

                                            if (fixedGeometry is Polygon)
                                            {
                                                // 如果修复后的结果是 Polygon，直接使用
                                                roomPolygon = (Polygon)fixedGeometry;
                                                // 使用 fixedPolygon 进行后续操作
                                            }
                                            else if (fixedGeometry is MultiPolygon multiPolygon && multiPolygon.NumGeometries == 1)
                                            {
                                                // 如果修复后的结果是 MultiPolygon 且只包含一个 Polygon，取出这个 Polygon
                                                roomPolygon = (Polygon)multiPolygon.GetGeometryN(0);
                                                // 使用 singlePolygon 进行后续操作
                                            }
                                        }

                                        if (!slabPolygon.IsValid)
                                        {
                                            slabPolygon = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(slabPolygon) as Polygon;
                                        }


                                        //返回结果不正确
                                        var intersectionGeometry = roomPolygon.Intersection(slabPolygon);



                                        if (intersectionGeometry.OgcGeometryType == OgcGeometryType.Polygon)
                                        {
                                            if (intersectionGeometry.IsEmpty) continue;
                                            if (slab.Thickness > 0 && slab.Thickness < 9999)
                                            {
                                                intersectionPolygonDictionary.Add(intersectionGeometry, slab.Thickness);
                                            }
                                            else
                                            {
                                                intersectionPolygonDictionary.Add(intersectionGeometry, F7_WindowViewModel.Instance.SlabThickness);
                                            }
                                        }
                                    }

                                    slab.TranslateVertices(-vector3D);
                                }


                                //对梁窝集合按照面积进行排序
                                var orderedIntersectionPolygonDictionary = intersectionPolygonDictionary.OrderByDescending(g => g.Key.Area).ToDictionary(x => x.Key, x => x.Value);

                                //探测器插入点的集合
                                List<Coordinate> coordinates = new List<Coordinate>();

                                //无效的几何集合
                                List<Geometry> invalidGeometries = new List<Geometry>();
                                //根据多段线集合生成探测器
                                //模糊判断探测距梁边大于500，不然加入无效的集合
                                foreach (var item in orderedIntersectionPolygonDictionary)
                                {

                                    if (CanOffestDistance(item.Key, -600)) continue;

                                    invalidGeometries.Add(item.Key);
                                }

                                foreach (var orderedItem in orderedIntersectionPolygonDictionary)
                                {
                                    if (invalidGeometries.Contains(orderedItem.Key)) continue;
                                    invalidGeometries.Add(orderedItem.Key);
                                    //多边形内布置一个或多个点位
                                    if ((orderedItem.Key.Area / 1000000) > detectorInfo.Area4)
                                    {
                                        //判断结合是否超出探测器保护范围，超出保护范围为假，需要切分为n个子区域
                                        // 最大迭代次数，用于控制循环次数
                                        const int maxIterations = 10;

                                        if (IsProtected(orderedItem.Key, detectorInfo.Radius))
                                        {
                                            coordinates.Add(orderedItem.Key.Centroid.Coordinate);
                                        }
                                        else
                                        {
                                            int n = 2; // 初始分割数量

                                            for (int i = 0; i < maxIterations; i++)
                                            {
                                                // 使用 SplitPolygonOptimized 方法将几何图形分割成 n 个部分
                                                List<Geometry> splitGeometries = SplitPolygonOptimized(geometryFactory, orderedItem.Key, n, 100.0);

                                                bool isAllProtected = true; // 标志变量，用于检查所有分割后的部分是否都被保护

                                                // 检查每个分割后的部分是否都被保护
                                                foreach (var item in splitGeometries)
                                                {
                                                    if (!IsProtected(item, detectorInfo.Radius))
                                                    {
                                                        isAllProtected = false;
                                                        break;
                                                    }
                                                }

                                                // 如果所有分割后的部分都被保护，将它们的质心坐标添加到 coordinates 列表中并结束循环
                                                if (isAllProtected)
                                                {
                                                    foreach (var item in splitGeometries)
                                                    {
                                                        coordinates.Add(item.Centroid.Coordinate);
                                                    }
                                                    break;
                                                }

                                                n++; // 增加分割数量，继续下一轮迭代
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // 检查是否在保护范围内，如果是，则不需要切分
                                        if (IsProtected(orderedItem.Key, detectorInfo.Radius))
                                        {
                                            List<Geometry> beam600Geometries = new List<Geometry>();
                                            List<Geometry> beam200Geometries = new List<Geometry>();

                                            // 检查是否有其他区域在保护范围内
                                            foreach (var item in orderedIntersectionPolygonDictionary)
                                            {
                                                if (invalidGeometries.Contains(item.Key)) continue;

                                                // 检查其他区域是否在保护范围内
                                                if (!IsProtected(geometryFactory, orderedItem.Key, detectorInfo.Radius, item.Key)) continue;

                                                // 检查是否与其他区域相交，并获取相交的线段
                                                if (!(orderedItem.Key.Intersection(item.Key) is LineString lineString)) continue;
                                                //AddLine(lineString);
                                                // 遍历当前楼层的梁，检查线段上的点
                                                foreach (var beam in curFloorBeams)
                                                {

                                                    LineString beamLineString = beam.ToLineString(geometryFactory, precisionReducer);

                                                    // 创建一个坐标转换器
                                                    var translateTransform = new AffineTransformation();
                                                    translateTransform.Translate(vector3D.X, vector3D.Y);

                                                    // 应用转换到 beamLineString
                                                    var translatedBeamLineString = (LineString)translateTransform.Transform(beamLineString);

                                                    //// 如果需要，也可以应用相反的转换
                                                    //translateTransform = new AffineTransformation();
                                                    //translateTransform.Translate(-vector3D.X, -vector3D.Y);
                                                    //var originalBeamLineString = (LineString)translateTransform.Transform(translatedBeamLineString);
                                                    // 检查线段的起点和终点是否在梁上
                                                    //bool isPointOnLine1 = new DistanceOp(lineString.StartPoint, beamLineString).Distance() <= 1e-3;
                                                    //bool isPointOnLine2 = new DistanceOp(lineString.EndPoint, beamLineString).Distance() <= 1e-3;


                                                    if (lineString.Coordinates.All(pt => translatedBeamLineString.Distance(geometryFactory.CreatePoint(pt)) < 1e-1))
                                                    {
                                                        double height = 0;
                                                        if (beam.IsConcrete)
                                                        {
                                                            height = beam.Height - item.Value;
                                                        }
                                                        else
                                                        {
                                                            height = beam.Height;
                                                        }

                                                        // 根据高度将区域划分到不同的列表
                                                        if (height > 600)
                                                        {
                                                            break;
                                                        }

                                                        if (height >= 200 && height <= 600)
                                                        {
                                                            beam600Geometries.Add(item.Key);
                                                            break;
                                                        }
                                                        if (0 < height && height < 200)
                                                        {
                                                            beam200Geometries.Add(item.Key);
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                            // 如果没有200高度的区域，将中心坐标添加到坐标列表中
                                            if (beam200Geometries.Count == 0 && beam600Geometries.Count == 0)
                                            {
                                                coordinates.Add(orderedItem.Key.Centroid.Coordinate);
                                                continue;
                                            }

                                            double area = orderedItem.Key.Area / 1000000;

                                            // 添加200高度的区域面积并将它们标记为无效
                                            if (beam200Geometries.Count > 0)
                                            {
                                                foreach (var item in beam200Geometries)
                                                {
                                                    area += item.Area / 1000000;
                                                    invalidGeometries.Add(item);
                                                }
                                            }

                                            // 根据区域面积选择切分数量并进行切分
                                            if (detectorInfo.Area3 < area && area <= detectorInfo.Area4)
                                            {
                                                int count = 2;
                                                IsProtectedByGeometry(invalidGeometries, beam600Geometries, count);
                                            }
                                            else if (detectorInfo.Area2 < area && area <= detectorInfo.Area3)
                                            {
                                                int count = 3;
                                                IsProtectedByGeometry(invalidGeometries, beam600Geometries, count);
                                            }
                                            else if (detectorInfo.Area1 < area && area <= detectorInfo.Area2)
                                            {
                                                int count = 4;
                                                IsProtectedByGeometry(invalidGeometries, beam600Geometries, count);
                                            }
                                            else if (area < detectorInfo.Area1)
                                            {
                                                int count = 5;
                                                IsProtectedByGeometry(invalidGeometries, beam600Geometries, count);
                                            }

                                            Coordinate coordinate = orderedItem.Key.Centroid.Coordinate;
                                            coordinates.Add(coordinate);
                                        }
                                        else
                                        {
                                            // 如果不在保护范围内，逐步增加切分数量，直到所有部分都被保护
                                            int n = 2;
                                            while (true)
                                            {
                                                List<Geometry> splitGeometries = SplitPolygon(geometryFactory, orderedItem.Key, n, 100.0);
                                                bool b = true;

                                                // 检查每个切分后的部分是否都被保护
                                                foreach (var item in splitGeometries)
                                                {
                                                    if (!IsProtected(item, detectorInfo.Radius))
                                                    {
                                                        b = false;
                                                        break;
                                                    }
                                                }

                                                // 如果所有部分都被保护，将它们的质心坐标添加到坐标列表中并结束循环
                                                if (b)
                                                {
                                                    foreach (var item in splitGeometries)
                                                    {
                                                        coordinates.Add(item.Centroid.Coordinate);
                                                    }
                                                    break;
                                                }

                                                n++;
                                            }
                                        }

                                    }
                                }
                                //去除距梁边小于500mm的布置点
                                if (coordinates.Count == 0)
                                {
                                    coordinates.Add(roomPolygon.Centroid.Coordinate);
                                }
                                ObjectId id;
                                if (detectorInfo.Radius == 3600 || detectorInfo.Radius == 4400)
                                {
                                    id = temperatureDetectorId;
                                }
                                else
                                {
                                    id = smokeDetectorId;
                                }
                                foreach (var coordinate in coordinates)
                                {
                                    BlockReference blockReference = new BlockReference(new Point3d(coordinate.X, coordinate.Y, 0), id);
                                    blockReferences.Add(blockReference);
                                    blockReference.ScaleFactors = new Scale3d(100);
                                    database.AddToModelSpace(blockReference);
                                }
                            }
                        }

                        #region 生成梁
                        foreach (var beam in curFloorBeams)
                        {
                            Point2d p1 = new Point2d(beam.Grid.Joint1.X, beam.Grid.Joint1.Y);
                            Point2d p2 = new Point2d(beam.Grid.Joint2.X, beam.Grid.Joint2.Y);
                            double startWidth = 0;
                            double endWidth = 0;
                            double height = 0;

                            Polyline polyline = new Polyline();

                            string[] shapeVals = beam.BeamSect.ShapeVal.Split(',');
                            startWidth = endWidth = double.Parse(shapeVals[1]);
                            height = double.Parse(shapeVals[2]);
                            string layerName = $"beam-steel-{beam.BeamSect.Kind}-{height}mm";

                            switch (beam.BeamSect.Kind)
                            {
                                case 1:
                                    layerName = $"beam-concrete-{height}mm";
                                    break;
                                case 22:
                                    height = Math.Min(double.Parse(shapeVals[3]), double.Parse(shapeVals[4]));
                                    break;
                                case 26:
                                    startWidth = endWidth = double.Parse(shapeVals[5]);
                                    break;
                            }

                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                            SetCurrentLayer(database, layerName, 7);

                            polyline.TransformBy(Matrix3d.Displacement(vector3D));
                            database.AddToModelSpace(polyline);
                        }

                        #endregion

                        #region 生成墙
                        foreach (var wall in curFloorWalls)
                        {
                            SetCurrentLayer(database, "wall", 53);

                            Point2d p1 = new Point2d(wall.Grid.Joint1.X, wall.Grid.Joint1.Y);
                            Point2d p2 = new Point2d(wall.Grid.Joint2.X, wall.Grid.Joint2.Y);
                            int startWidth = wall.WallSect.B;
                            int endWidth = wall.WallSect.B;

                            Polyline polyline = new Polyline();
                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                            polyline.TransformBy(Matrix3d.Displacement(vector3D));
                            database.AddToModelSpace(polyline);
                        }
                        #endregion
                    }
                    #endregion
                }
                catch
                {
                    transaction.Abort();
                    editor.WriteMessage("\n布置点位时发生错误");
                }
                transaction.Commit();
            }
            editor.WriteMessage("\n结束");
        }

        private void AddLine(LineString lineString)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            // 开启事务以便在数据库中进行操作
            using (Transaction trans = database.TransactionManager.StartTransaction())
            {
                // 打开模型空间以进行写操作
                BlockTable blockTable = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord modelSpace = trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // 创建Polyline对象
                Polyline polyline = new Polyline();

                // 将LineString的所有点添加到Polyline中
                foreach (Coordinate coordinate in lineString.Coordinates)
                {
                    Point2d point = new Point2d(coordinate.X, coordinate.Y); // 将Point3d转换为Point2d
                    polyline.AddVertexAt(polyline.NumberOfVertices, point, 0, 0, 0);
                }

                // 将新Polyline添加到模型空间记录并事务中
                modelSpace.AppendEntity(polyline);
                trans.AddNewlyCreatedDBObject(polyline, true);

                // 提交事务
                trans.Commit();
            }
        }

        private List<Point3d> GetPoint3DCollection(BlockReference blockReference)
        {
            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            List<Point3d> point3DList = new List<Point3d>();

            using (Transaction transaction = database.TransactionManager.StartOpenCloseTransaction())
            {
                Matrix3d matrix3D = blockReference.BlockTransform;

                BlockTableRecord btr = transaction.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null) return point3DList;

                foreach (DBPoint dBPoint in btr.OfType<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<DBPoint>())
                {
                    point3DList.Add(dBPoint.Position.TransformBy(matrix3D));
                }
            }

            return point3DList;
        }


        private ObjectId LoadBlockIntoDatabase(string blockPath)
        {
            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            using (Database blockDatabase = new Database(false, true))
            {
                using (Transaction transaction = blockDatabase.TransactionManager.StartTransaction())
                {
                    try
                    {
                        blockDatabase.ReadDwgFile(blockPath, FileOpenMode.OpenForReadAndReadShare, allowCPConversion: true, null);
                        blockDatabase.CloseInput(true);
                        transaction.Commit();
                        return database.Insert(Path.GetFileNameWithoutExtension(blockPath), blockDatabase, true);
                    }
                    catch
                    {
                        transaction.Abort();
                        editor.WriteMessage("\n加载AutoCAD图块发生错误");
                        return ObjectId.Null;
                    }
                }
            }
        }


        private HashSet<string> GetRoomType(string input)
        {
            HashSet<string> results = new HashSet<string>();
            string pattern = "A[12]H[23]";

            MatchCollection matches = Regex.Matches(input, pattern);
            foreach (Match match in matches)
            {
                results.Add(match.Value);
            }

            return results;
        }

        public static bool IsPointOnSegment(Coordinate point, Coordinate segmentStart, Coordinate segmentEnd, float tolerance)
        {
            Vector2 segment = new Vector2((float)(segmentEnd.X - segmentStart.X), (float)(segmentEnd.Y - segmentStart.Y));
            Vector2 pointToStart = new Vector2((float)(point.X - segmentStart.X), (float)(point.Y - segmentStart.Y));
            Vector2 pointToEnd = new Vector2((float)(point.X - segmentEnd.X), (float)(point.Y - segmentEnd.Y));

            float dotProduct = Vector2.Dot(segment, pointToStart);
            if (dotProduct < 0) return false;

            float squaredLength = segment.LengthSquared();
            if (dotProduct > squaredLength) return false;

            float crossProduct = segment.X * pointToEnd.Y - segment.Y * pointToEnd.X;
            return Math.Abs(crossProduct) <= tolerance;
        }

        private bool IsSegmentAOverlappingOrPartOfSegmentB(LineString A, LineString B, double tolerance)
        {
            // 获取第一个LineString的起点和终点
            Coordinate AStart = A.GetCoordinateN(0);
            Coordinate AEnd = A.GetCoordinateN(A.NumPoints - 1);

            // 获取第二个LineString的起点和终点
            Coordinate BStart = B.GetCoordinateN(0);
            Coordinate BEnd = B.GetCoordinateN(B.NumPoints - 1);

            // 判断第一个LineString的起点和终点是否在第二个LineString上
            bool startOnB = BStart.Distance(AStart) <= tolerance || BStart.Distance(AEnd) <= tolerance;
            bool endOnB = BEnd.Distance(AStart) <= tolerance || BEnd.Distance(AEnd) <= tolerance;

            // 判断第一个LineString是否与第二个LineString重合或是其一部分
            bool overlappingOrPartOfB = startOnB && endOnB;

            return overlappingOrPartOfB;
        }


        private bool CanOffestDistance(Geometry geometry, double distance)
        {
            //缓冲区参数
            var bufferParam = new BufferParameters
            {
                IsSingleSided = true,
                JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre,
            };

            Geometry bufferGeometry = geometry.Buffer(distance, bufferParam);

            if (bufferGeometry.Area > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static void IsProtectedByGeometry(List<Geometry> invalidGeometries, List<Geometry> beam600Geometries, int count)
        {
            int n = beam600Geometries.Count;
            if (n > 0)
            {
                var orderedBeam600Geometries = beam600Geometries.OrderByDescending(a => a.Area);
                if (n >= count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        invalidGeometries.Add(beam600Geometries[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        invalidGeometries.Add(beam600Geometries[i]);
                    }
                }
            }
        }

        private string GetAreaFilePath()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "区域数据库(*.area)|*.area|所有文件|*.*",
                Title = "选择区域数据库文件",
                ValidateNames = true,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            string name;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                name = openFileDialog.FileName;
            }
            else
            {
                return null;
            }
            return name;
        }

        private void SetCurrentLayer(Database database, string layerName, int colorIndex)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                
                LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

                // Create new layer if it doesn't exist
                ObjectId layerId;
                if (!layerTable.Has(layerName))
                {
                    LayerTableRecord layerTableRecord = new LayerTableRecord
                    {
                        Name = layerName
                    };

                    layerTable.UpgradeOpen();
                    layerId = layerTable.Add(layerTableRecord);
                    transaction.AddNewlyCreatedDBObject(layerTableRecord, add: true);
                    layerTable.DowngradeOpen();
                }
                else
                {
                    layerId = layerTable[layerName];
                }

                // Set layer color
                LayerTableRecord layerTableRecordToModify = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForWrite);
                layerTableRecordToModify.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);

                // Set current layer
                database.Clayer = layerId;

                transaction.Commit();
            }
        }


        /// <summary>
        /// 判断几何是否在指定半径及以几何质心为圆心的圆的保护范围内
        /// </summary>
        /// <param name="geometry">几何</param>
        /// <param name="radius">半径</param>
        /// <returns>true：在保护范围内；false:不在保护范围内</returns>
        private bool IsProtected(Geometry geometry, double radius)
        {
            MinimumBoundingCircle mbc = new MinimumBoundingCircle(geometry);
            Geometry circlePolygon = mbc.GetCircle();

            // 计算几何质心与最小外接圆边界上的一个点之间的距离作为最小外接圆半径
            Point centerPoint = circlePolygon.Centroid;
            Coordinate[] circleCoordinates = circlePolygon.Coordinates;
            double minRadius = centerPoint.Coordinate.Distance(circleCoordinates[0]);

            return minRadius <= radius;
        }

        public Geometry Validate(Geometry geom)
        {
            if (geom.OgcGeometryType == OgcGeometryType.Polygon)
            {
                if (geom.IsValid)
                {
                    geom.Normalize();
                    return geom;
                }
                Polygonizer polygonizer = new Polygonizer();
                AddPolygon((Polygon)geom, polygonizer);
                return ToPolygonGeometry(polygonizer.GetPolygons(), geom.Factory);
            }
            else if (geom.OgcGeometryType == OgcGeometryType.MultiPolygon)
            {
                if (geom.IsValid)
                {
                    geom.Normalize(); // validate does not pick up rings in the wrong order - this will fix that
                    return geom; // If the multipolygon is valid just return it
                }
                Polygonizer polygonizer = new Polygonizer();
                for (int i = 0; i < geom.NumGeometries; i++)
                {
                    AddPolygon((Polygon)geom.GetGeometryN(i), polygonizer);
                }

                return ToPolygonGeometry(polygonizer.GetPolygons(), geom.Factory);
            }
            else
            {
                return geom; // In my case, I only care about polygon / multipolygon geometries
            }
        }

        /**
          * Add all line strings from the polygon given to the polygonizer given
          * @param polygon polygon from which to extract line strings
          * @param polygonizer polygonizer
          */
        private void AddPolygon(Polygon polygon, Polygonizer polygonizer)
        {
            //添加外边界线
            AddLineString(polygon.ExteriorRing, polygonizer);
            //添加内边界线
            foreach (var line in polygon.InteriorRings)
            {
                AddLineString(line, polygonizer);
            }
        }

        /**
         * Add the linestring given to the polygonizer
         * @param linestring line string
         * @param polygonizer polygonizer
         */
        private void AddLineString(LineString lineString, Polygonizer polygonizer)
        {
            // LinearRings are treated differently to line strings : we need a LineString NOT a LinearRing
            lineString = lineString.Factory.CreateLineString(lineString.CoordinateSequence);
            // unioning the linestring with the point makes any self intersections explicit.

            Geometry toAdd = null;
            Point point = lineString.Factory.CreatePoint(lineString.GetCoordinateN(0));
            toAdd = lineString.Union(point);
            //for (int i = 0; i < lineString.NumPoints; i++)
            //{
            //    IPoint point = lineString.Factory.CreatePoint(lineString.GetCoordinateN(i));
            //    toAdd = lineString.Union(point);
            //}

            //Add result to polygonizer
            polygonizer.Add(toAdd);

            return;

        }

        /**
         * Get a geometry from a collection of polygons.
         * 
         * @param polygons collection
         * @param factory factory to generate MultiPolygon if required
         * @return null if there were no polygons, the polygon if there was only one, or a MultiPolygon containing all polygons otherwise
         */
        private Geometry ToPolygonGeometry(ICollection<Geometry> polygons, GeometryFactory factory)
        {
            switch (polygons.Count)
            {
                case 0:
                    return null; // No valid polygons!
                case 1:
                    return polygons.ElementAt(0); // single polygon - no need to wrap
                default:
                    return factory.CreateMultiPolygon(polygons.Select(p => p as Polygon).ToArray()); // multiple polygons - wrap them
            }
        }


        /// <summary>
        /// 判断区域是否在相邻区域探测器的保护范围内
        /// </summary>
        /// <param name="geometry">相邻的区域</param>
        /// <param name="radius">保护半径</param>
        /// <param name="touchGeometry">被判断的区域</param>
        /// <returns>true：在保护范围内；false:不在保护范围内</returns>
        private bool IsProtected(GeometryFactory geometryFactory, Geometry geometry, double radius, Geometry touchGeometry)
        {
            Point centerPoint = geometry.Centroid;
            bool b = true;
            for (int i = 0; i < touchGeometry.NumPoints - 1; i++)
            {

                double d = centerPoint.Coordinate.Distance(touchGeometry.Coordinates[i]);
                if (d > radius)
                {
                    b = false;
                }
            }
            return b;
        }

        /// <summary>
        /// 获取板轮廓线点的集合
        /// </summary>
        /// <param name="slab">板</param>
        /// <returns>点集合</returns>
        private Coordinate[] GetSlabCoordinates(Slab slab)
        {
            string[] vertexX = slab.VertexX.Split(',');
            string[] vertexY = slab.VertexY.Split(',');
            int n = vertexX.Length;

            Coordinate[] coordinates = new Coordinate[n + 1];
            for (int i = 0; i < n + 1; i++)
            {

                coordinates[i] = new Coordinate(double.Parse(vertexX[i % n]), double.Parse(vertexY[i % n]));
            }
            return coordinates;
        }

        /// <summary>
        /// 获取YDB文件
        /// </summary>
        /// <returns>返回文件路径</returns>
        private string GetYdbFilePath()
        {
            //List<string> list = new List<string>();
            string name = null;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "盈建科数据库(*.ydb)|*.ydb|所有文件|*.*";
            openFileDialog.Title = "选择结构模型数据库文件";
            openFileDialog.ValidateNames = true;
            openFileDialog.CheckFileExists = true;
            openFileDialog.CheckPathExists = true;
            openFileDialog.Multiselect = false;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                name = openFileDialog.FileName;
                //list.AddRange(dbPaths);
            }
            else
            {
                return null;
            }
            return name;
        }


        /// <summary>
        /// 获取房间轮廓线点的集合
        /// </summary>
        /// <param name="slab">板</param>
        /// <returns>点集合</returns>
        private Coordinate[] GetAreaCoordinates(Model.Area roomAear)
        {
            int n = roomAear.VertexX.Split(',').Length;
            Coordinate[] coordinates = new Coordinate[n + 1];
            for (int i = 0; i < n + 1; i++)
            {
                coordinates[i] = new Coordinate(Math.Round(double.Parse(roomAear.VertexX.Split(',')[i % n]), 3), Math.Round(double.Parse(roomAear.VertexY.Split(',')[i % n]), 3));
            }
            return coordinates;
        }

        /// <summary>
        /// 判断两个矩形是否相交和包含
        /// </summary>
        /// <param name="roomExtents3d">房间外轮廓</param>
        /// <param name="slabExtents3d">板外轮廓</param>
        /// <returns></returns>
        private bool CheckCross(Extents3d roomExtents3d, Extents3d slabExtents3d)
        {
            Point2d roomMaxPoint = roomExtents3d.MaxPoint.ToPoint2d();
            Point2d roomMinPoint = roomExtents3d.MinPoint.ToPoint2d();

            Point2d slabMaxPoint = slabExtents3d.MaxPoint.ToPoint2d();
            Point2d slabMinPoint = slabExtents3d.MinPoint.ToPoint2d();

            return ((Math.Abs(roomMaxPoint.X + roomMinPoint.X - slabMaxPoint.X - slabMinPoint.X) < roomMaxPoint.X - roomMinPoint.X + slabMaxPoint.X - slabMinPoint.X)
                && Math.Abs(roomMaxPoint.Y + roomMinPoint.Y - slabMaxPoint.Y - slabMinPoint.Y) < roomMaxPoint.Y - roomMinPoint.Y + slabMaxPoint.Y - slabMinPoint.Y);
        }

        /// <summary>
        /// 判断两个多边形是否有交点
        /// </summary>
        /// <param name="roomPolyline"></param>
        /// <param name="slabPolyline"></param>
        /// <returns></returns>
        private Point3dCollection GetIntersectPoint3d(Polyline roomPolyline, Polyline slabPolyline)
        {
            Point3dCollection intPoints3d = new Point3dCollection();
            for (int i = 0; i < slabPolyline.NumberOfVertices; i++)
            {
                if (i < slabPolyline.NumberOfVertices - 1 || slabPolyline.Closed)
                {
                    SegmentType slabSegmentType = slabPolyline.GetSegmentType(i);
                    if (slabSegmentType == SegmentType.Line)
                    {
                        LineSegment2d slabLine = slabPolyline.GetLineSegment2dAt(i);
                        PolyIntersectWithLine(roomPolyline, slabLine, 0.0001, ref intPoints3d);
                    }
                    else if (slabSegmentType == SegmentType.Arc)
                    {

                    }
                }
            }
            return intPoints3d;
        }


        /// <summary>
        /// 多段线和直线求交点
        /// </summary>
        /// <param name="poly"></param>
        /// <param name="geLine"></param>
        /// <param name="tol"></param>
        /// <param name="points"></param>
        private void PolyIntersectWithLine(Polyline poly, LineSegment2d geLine, double tol, ref Point3dCollection points)
        {
            Point2dCollection intPoints2d = new Point2dCollection();

            // 获得直线对应的几何类
            //LineSegment2d geLine = new LineSegment2d(ToPoint2d(line.StartPoint), ToPoint2d(line.EndPoint));

            // 每一段分别计算交点
            Tolerance tolerance = new Tolerance(tol, tol);
            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                if (i < poly.NumberOfVertices - 1 && poly.Closed)
                {
                    SegmentType st = poly.GetSegmentType(i);
                    if (st == SegmentType.Line)
                    {
                        LineSegment2d geLineSeg = poly.GetLineSegment2dAt(i);
                        Point2d[] pts = geLineSeg.IntersectWith(geLine, tolerance);
                        if (pts != null)
                        {
                            for (int j = 0; j < pts.Length; j++)
                            {
                                if (FindPointIn(intPoints2d, pts[j], tol) < 0)
                                {
                                    intPoints2d.Add(pts[j]);
                                }
                            }
                        }
                    }
                    else if (st == SegmentType.Arc)
                    {
                        CircularArc2d geArcSeg = poly.GetArcSegment2dAt(i);
                        Point2d[] pts = geArcSeg.IntersectWith(geLine, tolerance);
                        if (pts != null)
                        {
                            for (int j = 0; j < pts.Length; j++)
                            {
                                if (FindPointIn(intPoints2d, pts[j], tol) < 0)
                                {
                                    intPoints2d.Add(pts[j]);
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < intPoints2d.Count; i++)
            {
                points.Add(ToPoint3d(intPoints2d[i]));
            }
        }


        // 点是否在集合中
        private int FindPointIn(Point2dCollection points, Point2d pt, double tol)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - pt.X) < tol && Math.Abs(points[i].Y - pt.Y) < tol)
                {
                    return i;
                }
            }

            return -1;
        }

        // 三维点转二维点
        private static Point2d ToPoint2d(Point3d point3d)
        {
            return new Point2d(point3d.X, point3d.Y);
        }

        // 二维点转三维点
        private static Point3d ToPoint3d(Point2d point2d)
        {
            return new Point3d(point2d.X, point2d.Y, 0);
        }

        private static Point3d ToPoint3d(Point2d point2d, double elevation)
        {
            return new Point3d(point2d.X, point2d.Y, elevation);
        }


        /// <summary>
        /// 多边形点集排序
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public Point3dCollection SortPolyPoints(Point3dCollection points)
        {

            if (points == null || points.Count == 0) return null;
            //计算重心
            Point3d center = new Point3d();
            double X = 0, Y = 0;
            for (int i = 0; i < points.Count; i++)
            {
                X += points[i].X;
                Y += points[i].Y;
            }
            center = new Point3d((int)X / points.Count, (int)Y / points.Count, points[0].Z);
            //冒泡排序
            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int j = 0; j < points.Count - i - 1; j++)
                {
                    if (PointCmp(points[j], points[j + 1], center))
                    {
                        Point3d tmp = points[j];
                        points[j] = points[j + 1];
                        points[j + 1] = tmp;
                    }
                }
            }
            return points;
        }

        /// <summary>
        /// 若点a大于点b,即点a在点b顺时针方向,返回true,否则返回false
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="center"></param>
        /// <returns></returns>
        private bool PointCmp(Point3d a, Point3d b, Point3d center)
        {
            if (a.X >= 0 && b.X < 0)
                return true;
            else if (a.X == 0 && b.X == 0)
                return a.Y > b.Y;
            //向量OA和向量OB的叉积
            double det = (a.X - center.X) * (b.Y - center.Y) - (b.X - center.X) * (a.Y - center.Y);
            if (det < 0)
                return true;
            if (det > 0)
                return false;
            //向量OA和向量OB共线，以距离判断大小
            double d1 = (a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y);
            double d2 = (b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y);
            return d1 > d2;
        }

        /// <summary>
        /// 按照指定数量切分多边形
        /// </summary>
        /// <param name="geometry">多边形</param>
        /// <param name="count">平分份数</param>
        /// <param name="n">点集数量（kmeans算法用，越多越精确但速度越慢）</param>
        /// <param name="detectorInfo">探测器</param>
        /// <returns>符合条件的平分多边形的质心点集</returns>
        List<Geometry> SplitPolygon(GeometryFactory geometryFactory, Geometry geometry, int count, int n)
        {
            Random random = new Random();
            //质心点集
            //List<Point2d> Points = new List<Point2d>();
            Coordinate maxPoint = geometry.Max();
            Coordinate minPoint = geometry.Min();

            //构建随机点并判断点是否在多边形内部
            double[][] randomPoints = new double[n][];
            for (int i = 0; i < n; i++)
            {
                while (true)
                {
                    double x = minPoint.X + random.NextDouble() * (maxPoint.X - minPoint.X);
                    double y = minPoint.Y + random.NextDouble() * (maxPoint.Y - minPoint.Y);
                    Point point2D = geometryFactory.CreatePoint(new Coordinate(x, y));

                    if (point2D.Within(geometry))
                    {
                        randomPoints[i] = new double[2] { x, y };
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            //利用EKmeans 获取分组和簇的质心
            Accord.Math.Random.Generator.Seed = 0;
            KMeans kMeans = new KMeans(count);
            KMeansClusterCollection clusters = kMeans.Learn(randomPoints);
            double[][] centerPoints = clusters.Centroids;
            List<Coordinate> coords = new List<Coordinate>();
            foreach (var c in centerPoints)
            {
                coords.Add(new Coordinate(c[0], c[1]));
            }
            //构建泰森多边形
            VoronoiDiagramBuilder voronoiDiagramBuilder = new VoronoiDiagramBuilder();
            Envelope clipEnvelpoe = new Envelope(minPoint, maxPoint);
            voronoiDiagramBuilder.ClipEnvelope = clipEnvelpoe;
            voronoiDiagramBuilder.SetSites(coords);
            GeometryCollection geometryCollection = voronoiDiagramBuilder.GetDiagram(geometryFactory);

            // 4. 利用封闭面切割泰森多边形
            List<Geometry> geometries = new List<Geometry>();
            for (int i = 0; i < geometryCollection.NumGeometries; i++)
            {
                Geometry vorGeometry = geometryCollection.GetGeometryN(i);
                geometries.Add(vorGeometry.Intersection(geometry));
            }
            return geometries;
        }

        List<Geometry> SplitPolygon(GeometryFactory geometryFactory, Geometry geometry, int count, double interval)
        {
            // 质心点集
            Coordinate maxPoint = geometry.Max();
            Coordinate minPoint = geometry.Min();

            // 构建网格点并判断点是否在多边形内部
            List<double[]> gridPoints = new List<double[]>();
            for (double x = minPoint.X; x <= maxPoint.X; x += interval)
            {
                for (double y = minPoint.Y; y <= maxPoint.Y; y += interval)
                {
                    Point point2D = geometryFactory.CreatePoint(new Coordinate(x, y));

                    if (point2D.Within(geometry))
                    {
                        gridPoints.Add(new double[2] { x, y });
                    }
                }
            }

            // 利用EKmeans 获取分组和簇的质心
            Accord.Math.Random.Generator.Seed = 0;
            KMeans kMeans = new KMeans(count);
            KMeansClusterCollection clusters = kMeans.Learn(gridPoints.ToArray());
            double[][] centerPoints = clusters.Centroids;
            List<Coordinate> coords = new List<Coordinate>();
            foreach (var c in centerPoints)
            {
                coords.Add(new Coordinate(c[0], c[1]));
            }

            // 构建泰森多边形
            VoronoiDiagramBuilder voronoiDiagramBuilder = new VoronoiDiagramBuilder();
            Envelope clipEnvelpoe = new Envelope(minPoint, maxPoint);
            voronoiDiagramBuilder.ClipEnvelope = clipEnvelpoe;
            voronoiDiagramBuilder.SetSites(coords);
            GeometryCollection geometryCollection = voronoiDiagramBuilder.GetDiagram(geometryFactory);

            // 利用封闭面切割泰森多边形
            List<Geometry> geometries = new List<Geometry>();
            for (int i = 0; i < geometryCollection.NumGeometries; i++)
            {
                Geometry vorGeometry = geometryCollection.GetGeometryN(i);
                geometries.Add(vorGeometry.Intersection(geometry));
            }
            return geometries;
        }

        private List<Geometry> SplitPolygonOptimized(GeometryFactory geometryFactory, Geometry geometry, int count, double interval)
        {
            // 计算几何图形的外包围盒（Envelope）
            Envelope envelope = geometry.EnvelopeInternal;

            // 计算最大和最小点的坐标
            double minX = envelope.MinX;
            double minY = envelope.MinY;
            double maxX = envelope.MaxX;
            double maxY = envelope.MaxY;

            // 计算分割点的数量，避免过多的点
            int numX = (int)((maxX - minX) / interval) + 1;
            int numY = (int)((maxY - minY) / interval) + 1;

            // 创建点列表
            List<Coordinate> gridPoints = new List<Coordinate>();

            // 构建网格点并判断点是否在多边形内部
            for (int i = 0; i < numX; i++)
            {
                for (int j = 0; j < numY; j++)
                {
                    double x = minX + i * interval;
                    double y = minY + j * interval;

                    if (geometry.Covers(geometryFactory.CreatePoint(new Coordinate(x, y))))
                    {
                        gridPoints.Add(new Coordinate(x, y));
                    }
                }
            }

            // 利用EKmeans 获取分组和簇的质心
            Accord.Math.Random.Generator.Seed = 0;
            KMeans kMeans = new KMeans(count);
            KMeansClusterCollection clusters = kMeans.Learn(gridPoints.Select(p => new double[] { p.X, p.Y }).ToArray());
            double[][] centerPoints = clusters.Centroids;

            // 构建泰森多边形
            VoronoiDiagramBuilder voronoiDiagramBuilder = new VoronoiDiagramBuilder
            {
                ClipEnvelope = envelope
            };
            voronoiDiagramBuilder.SetSites(centerPoints.Select(c => new Coordinate(c[0], c[1])).ToList());
            GeometryCollection geometryCollection = voronoiDiagramBuilder.GetDiagram(geometryFactory);

            // 利用封闭面切割泰森多边形
            List<Geometry> geometries = new List<Geometry>();
            for (int i = 0; i < geometryCollection.NumGeometries; i++)
            {
                Geometry vorGeometry = geometryCollection.GetGeometryN(i);
                Geometry intersection = vorGeometry.Intersection(geometry);
                if (!intersection.IsEmpty)
                {
                    geometries.Add(intersection);
                }
            }

            return geometries;
        }


    }
}