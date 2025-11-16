using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DotNetARX;

using NetTopologySuite.Geometries;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using Google.OrTools.ConstraintSolver;


using TimeIsLife.Model;
using TimeIsLife.ViewModel;
using Google.Protobuf.WellKnownTypes;
using TimeIsLife.Helper;
using TimeIsLife.View;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("F8_EquipmentConnectLine")]
        public void F8_EquipmentConnectLine()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            string message = "\n作用：选择连线形式对设备进行连线" +
                                  "\n操作方法：根据命令提示，选择连线形式" +
                                  "\n注意事项：防火分区多段线需要单独图层，防火分区多段线内需要文字标注防火分区编号，文字和多段线需要同一个图层";
            editor.WriteMessage(message);



            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                try
                {
                    //获取块表
                    BlockTable blockTable = transaction.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    //获取模型空间
                    BlockTableRecord modelSpace = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                    //获取图纸空间
                    BlockTableRecord paperSpace = transaction.GetObject(blockTable[BlockTableRecord.PaperSpace], OpenMode.ForRead) as BlockTableRecord;

                    List<string> layerNames1 = new List<string>() {
                        MyPlugin.CurrentUserData.FireAreaLayerName,
                        MyPlugin.CurrentUserData.EquipmentLayerName,
                        //MyPlugin.CurrentUserData.WireLayerName
                    };
                    if (MyPlugin.CurrentUserData.IsUseAvoidanceArea)
                    {
                        layerNames1.Add(MyPlugin.CurrentUserData.AvoidanceAreaLayerName);
                    }
                    
                    LayerTable layerTable = transaction.GetObject(database.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (layerTable == null) return;
                    if (!CheckAllLayers(layerTable, layerNames1))
                    {
                        F8_Window.Instance.ShowDialog();
                        if (F8_WindowViewModel.Instance.Result)
                        {
                            List<string> layerNames2 = new List<string>() {
                                MyPlugin.CurrentUserData.FireAreaLayerName,
                                MyPlugin.CurrentUserData.EquipmentLayerName,
                                //MyPlugin.CurrentUserData.WireLayerName
                            };
                            if (MyPlugin.CurrentUserData.IsUseAvoidanceArea)
                            {
                                layerNames1.Add(MyPlugin.CurrentUserData.AvoidanceAreaLayerName);
                            }
                            if (!CheckAllLayers(layerTable, layerNames2))
                            {
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }

                    bool bo = true;
                    while (bo)
                    {
                        // 添加用户输入来选择连线类型
                        PromptKeywordOptions keywordOptions = new PromptKeywordOptions("\n选择连线类型 [环形(Circle)/树形(Tree)/选项(Options)]: ");
                        keywordOptions.Keywords.Add("Circle");
                        keywordOptions.Keywords.Add("Tree");
                        keywordOptions.Keywords.Add("Options");
                        keywordOptions.Keywords.Default = MyPlugin.CurrentUserData.TreeOrCircle;

                        PromptResult keywordResult = editor.GetKeywords(keywordOptions);
                        if (keywordResult.Status != PromptStatus.OK) return;
                        switch (keywordResult.StringResult)
                        {
                            case "Circle":
                                MyPlugin.CurrentUserData.TreeOrCircle = keywordResult.StringResult;
                                bo = false;
                                break;
                            case "Tree":
                                MyPlugin.CurrentUserData.TreeOrCircle = keywordResult.StringResult;
                                bo = false;
                                break;
                            case "Options":
                                F8_Window.Instance.ShowDialog();
                                if (F8_WindowViewModel.Instance.Result)
                                {
                                    List<string> layerNames3 = new List<string>
                                    {
                                        MyPlugin.CurrentUserData.FireAreaLayerName,
                                        MyPlugin.CurrentUserData.AvoidanceAreaLayerName,
                                        MyPlugin.CurrentUserData.EquipmentLayerName,
                                        //MyPlugin.CurrentUserData.WireLayerName
                                    };
                                    if (!CheckAllLayers(layerTable, layerNames3))
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    return;
                                }
                                break;
                        }
                    }
                    //选择选定图层上的所有多段线
                    TypedValueList fireAreaTypedValues = new TypedValueList
                    {
                        { DxfCode.LayerName, MyPlugin.CurrentUserData.FireAreaLayerName },
                        typeof(Polyline)
                    };
                    SelectionFilter fireAreaSelectionFilter = new SelectionFilter(fireAreaTypedValues);
                    PromptSelectionResult fireAreaSelectionResult = editor.SelectAll(fireAreaSelectionFilter);
                    if (fireAreaSelectionResult.Status != PromptStatus.OK) return;

                    SetCurrentLayer(database, MyPlugin.CurrentUserData.WireLayerName, 1);

                    //迭代防火分区
                    foreach (var id in fireAreaSelectionResult.Value.GetObjectIds())
                    {
                        List<BlockReference> blockReferences = new List<BlockReference>();

                        Polyline fireAreaPolyline =
                            transaction.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (fireAreaPolyline == null) continue;
                        Point3dCollection point3DCollection = fireAreaPolyline.GetPoint3dCollection();

                        TypedValueList blockReferenceTypedValues = new TypedValueList
                                {
                                    typeof(BlockReference),
                                    { DxfCode.LayerName, MyPlugin.CurrentUserData.EquipmentLayerName }
                                };
                        SelectionFilter blockReferenceSelectionFilter =
                            new SelectionFilter(blockReferenceTypedValues);
                        PromptSelectionResult blockReferenceSelectionResult =
                            editor.SelectCrossingPolygon(point3DCollection, blockReferenceSelectionFilter);
                        if (blockReferenceSelectionResult.Status != PromptStatus.OK) continue;

                        foreach (var blockReferenceId in blockReferenceSelectionResult.Value.GetObjectIds())
                        {
                            BlockReference blockReference =
                                transaction.GetObject(blockReferenceId, OpenMode.ForRead) as BlockReference;
                            if (blockReference == null) continue;
                            LayerTableRecord layerTableRecord =
                                transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as
                                    LayerTableRecord;
                            if (layerTableRecord != null && layerTableRecord.IsLocked) continue;
                            if (blockReference.GetConnectionPoints().Count == 0) continue;
                            blockReferences.Add(blockReference);
                        }
                        List<Coordinate> coordinates = blockReferences.Select(br => new Coordinate(br.Position.X, br.Position.Y)).ToList();
                        List<AvoidanceArea> avoidanceAreas = new List<AvoidanceArea>();
                        TypedValueList avoidanceAreaTypedValues = new TypedValueList
                        {
                            { DxfCode.LayerName, MyPlugin.CurrentUserData.AvoidanceAreaLayerName },
                            typeof(Polyline)
                        };
                        SelectionFilter avoidanceAreaSelectionFilter = new SelectionFilter(avoidanceAreaTypedValues);
                        PromptSelectionResult avoidanceAreaSelectionResult =
                            editor.SelectCrossingPolygon(point3DCollection, avoidanceAreaSelectionFilter);
                        if (avoidanceAreaSelectionResult.Status == PromptStatus.OK)
                        {
                            foreach (var objectId in avoidanceAreaSelectionResult.Value.GetObjectIds())
                            {
                                Polyline avoidanceAreaPolyline = transaction.GetObject(objectId, OpenMode.ForRead) as Polyline;
                                if (avoidanceAreaPolyline == null) continue;
                                avoidanceAreas.Add(new AvoidanceArea(avoidanceAreaPolyline.GetPolygonCoordinates()));
                            }
                        }

                        List<LineString> lineStrings;
                        const double tolerance = 1e-3;

                        if (MyPlugin.CurrentUserData.TreeOrCircle == "Circle")
                        {
                            lineStrings = avoidanceAreas.Count == 0 ?
                                CreateOptimalRingConnections(coordinates) :
                                CreateOptimalRingConnections(coordinates, avoidanceAreas);
                        }
                        else
                        {
                            lineStrings =
                                CreateMinimumSpanningTreeConnections(coordinates);
                        }

                        foreach (var lineString in lineStrings)
                        {
                            var startPoint = new Point3d(lineString.Coordinates[0].X,
                                lineString.Coordinates[0].Y, 0);
                            var endPoint = new Point3d(lineString.Coordinates[1].X, lineString.Coordinates[1].Y,
                                0);

                            BlockReference br1 = blockReferences.FirstOrDefault(b =>
                                Math.Abs(b.Position.X - startPoint.X) < tolerance &&
                                Math.Abs(b.Position.Y - startPoint.Y) < tolerance);
                            BlockReference br2 = blockReferences.FirstOrDefault(b =>
                                Math.Abs(b.Position.X - endPoint.X) < tolerance &&
                                Math.Abs(b.Position.Y - endPoint.Y) < tolerance);

                            Line connectline = BuildConnectLine(br1, br2);


                            modelSpace.AppendEntity(connectline);
                            transaction.AddNewlyCreatedDBObject(connectline, true);
                        }
                    }
                    transaction.Commit();
                }
                catch (System.Exception)
                {
                    // ignored
                }
            }
        }

        private List<LineString> CreateOptimalRingConnections(List<Coordinate> coordinates)
        {
            if (coordinates.Count < 3)
                throw new ArgumentException("至少需要3个块来形成环形连线。");

            // 将blocks转换为坐标点集
            var geometryFactory = new GeometryFactory();

            // OR-Tools 求解器的初始化
            RoutingIndexManager manager = new RoutingIndexManager(coordinates.Count, 1, 0);
            RoutingModel routing = new RoutingModel(manager);

            // 距离计算回调
            int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                // 转换索引为用户定义的点索引
                var fromNode = manager.IndexToNode(fromIndex);
                var toNode = manager.IndexToNode(toIndex);
                Coordinate p1 = coordinates[fromNode];
                Coordinate p2 = coordinates[toNode];
                return (long)Math.Round(Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2)));
            });

            routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

            // 设置搜索参数
            RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
            searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;

            // 解决问题
            Assignment solution = routing.SolveWithParameters(searchParameters);

            // 获取解决方案并创建LineString连接
            List<Coordinate> sortedPoints = new List<Coordinate>();
            long index = routing.Start(0);
            while (routing.IsEnd(index) == false)
            {
                sortedPoints.Add(coordinates[manager.IndexToNode(index)]);
                index = solution.Value(routing.NextVar(index));
            }
            sortedPoints.Add(sortedPoints[0]); // 闭合环路

            var connections = new List<LineString>();
            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                Coordinate start = sortedPoints[i];
                Coordinate end = sortedPoints[i + 1];
                connections.Add(geometryFactory.CreateLineString(new[] { sortedPoints[i], sortedPoints[i + 1] }));
            }

            return connections;
        }

        private List<LineString> CreateOptimalRingConnections(List<Coordinate> coordinates, List<AvoidanceArea> avoidanceAreas)
        {
            if (coordinates.Count < 3)
                throw new ArgumentException(@"至少需要3个块来形成环形连线。");

            // 将blocks转换为坐标点集
            var geometryFactory = new GeometryFactory();

            // OR-Tools 求解器的初始化
            RoutingIndexManager manager = new RoutingIndexManager(coordinates.Count, 1, 0);
            RoutingModel routing = new RoutingModel(manager);

            // 创建距离回调
            int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                var fromNode = manager.IndexToNode(fromIndex);
                var toNode = manager.IndexToNode(toIndex);
                var line = geometryFactory.CreateLineString(new[] { coordinates[fromNode], coordinates[toNode] });

                // 检查路径是否穿过任何避开区域
                foreach (var area in avoidanceAreas)
                {
                    if (area.Area.Intersects(line))
                    {
                        return long.MaxValue; // 通过设定非常高的成本来避开这个区域
                    }
                }

                // 计算并返回两点之间的距离
                return (long)coordinates[fromNode].Distance(coordinates[toNode]) * 1000; // 距离转换为整数
            });

            routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);

            // 设置搜索参数
            RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
            searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;

            // 解决问题
            Assignment solution = routing.SolveWithParameters(searchParameters);
            if (solution == null)
            {
                throw new InvalidOperationException("无法找到解决方案。");
            }

            // 获取解决方案并创建LineString连接
            List<Coordinate> sortedPoints = new List<Coordinate>();
            long currentIndex = routing.Start(0);
            while (!routing.IsEnd(currentIndex))
            {
                sortedPoints.Add(coordinates[manager.IndexToNode(currentIndex)]);
                currentIndex = solution.Value(routing.NextVar(currentIndex));
            }
            sortedPoints.Add(sortedPoints[0]); // 闭合环路

            var connections = new List<LineString>();
            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                connections.Add(geometryFactory.CreateLineString(new[] { sortedPoints[i], sortedPoints[i + 1] }));
            }

            return connections;
        }

        private List<LineString> CreateMinimumSpanningTreeConnections(List<Coordinate> coordinates)
        {
            if (coordinates.Count < 2)
                throw new ArgumentException("至少需要2个坐标点来形成树形连线。");

            GeometryFactory geometryFactory = new GeometryFactory();
            var points = coordinates.Select(coordinate => geometryFactory.CreatePoint(coordinate)).ToList();

            return Kruskal.FindMinimumSpanningTree(points, geometryFactory);
        }

        private bool CheckAllLayers(LayerTable layerTable, List<string> names)
        {
            foreach (var name in names)
            {
                if (!layerTable.Has(name))
                {
                    MessageBox.Show($@"缺失 {name} 图层!");
                    return false;
                }
            }
            return true;
        }
    }
}
