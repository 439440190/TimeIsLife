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
            try
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

                // 创建一个临时矩形，用于动态拖拽显示
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

                // 启动 Jig
                var jig = new UcsSelectJig(startPointWcs, rect);
                if (ed.Drag(jig).Status != PromptStatus.OK)
                    return;

                Point3d endPointWcs = jig.EndPointWcs;

                // 计算矩形的四个角点（WCS）
                Point3dCollection rectPts = GetRectPointsInWcs(startPointWcs, endPointWcs, ucs2wcs);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 获取块表与模型空间
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 框选所有块参照
                    TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                    SelectionFilter selFilter = new SelectionFilter(filter);

                    PromptSelectionResult psr = ed.SelectWindowPolygon(rectPts, selFilter);
                    if (psr.Status != PromptStatus.OK)
                    {
                        tr.Commit();
                        return;
                    }

                    // 收集有效的块参照
                    List<BlockReference> brList = new List<BlockReference>();
                    foreach (ObjectId id in psr.Value.GetObjectIds())
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;

                        LayerTableRecord layer = tr.GetObject(br.LayerId, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null && layer.IsLocked) continue;

                        brList.Add(br);
                    }

                    if (brList.Count < 2)
                    {
                        ed.WriteMessage("\n错误：框选对象少于2个块，无法连线。");
                        tr.Commit();
                        return;
                    }

                    // 创建几何工厂和几何点
                    GeometryFactory geometryFactory = CreateGeometryFactory();
                    var points = brList
                        .Select(br => geometryFactory.CreatePoint(
                            new Coordinate(br.Position.X, br.Position.Y)))
                        .Distinct()
                        .ToList();

                    if (points.Count < 2)
                    {
                        ed.WriteMessage("\n错误：有效的不同位置的块少于2个。");
                        tr.Commit();
                        return;
                    }

                    // 找到最小生成树
                    List<LineString> tree = Kruskal.FindMinimumSpanningTree(points, geometryFactory);
                    
                    // 设置当前图层
                    SetCurrentLayer(db, MyPlugin.CurrentUserData.WireLayerName, 1);
                    
                    const double tolerance = 1e-3;
                    int connectedCount = 0;
                    
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

                        if (br1 == null || br2 == null)
                            continue;

                        Line connectline = BuildConnectLine(br1, br2);
                        if (connectline == null)
                            continue;

                        ms.AppendEntity(connectline);
                        tr.AddNewlyCreatedDBObject(connectline, true);
                        connectedCount++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n已成功连接 {connectedCount} 条线。");
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n错误：{ex.Message}");
                // 可以在这里添加日志记录
            }
        }
    }
}
