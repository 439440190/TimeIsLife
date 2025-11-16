using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DotNetARX;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TimeIsLife.Helper;
using TimeIsLife.Jig;

using TimeIsLife.Model;

namespace TimeIsLife.CADCommand
{
    partial class TilCommand
    {
        #region F4_AlignUcs

        /// <summary>
        /// 命令方法：在UCS坐标系下沿X轴或Y轴对齐多个块参照
        /// </summary>
        /// <remarks>
        /// 操作流程：
        /// 1. 框选对象
        /// 2. 选择对齐方向（默认上次选择）
        /// 3. 选择基准对齐对象
        /// </remarks>
        [CommandMethod("F4_AlignUcs")]
        public void F4_AlignUcs()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                var db = doc.Database;
                var ed = doc.Editor;
                var ucs2wcs = ed.CurrentUserCoordinateSystem;

                ed.WriteMessage(
                    "\n作用：多个对象在UCS坐标系下，沿X轴或Y轴对齐。" +
                    "\n操作方法：框选对象，选择基准对齐对象。" +
                    "\n对齐方向：X(水平) 或 Y(垂直)，默认为上次选择。\n");

                // 第1步：获取对齐方向（直接使用默认值或上次的值）
                var alignDirection = MyPlugin.CurrentUserData.AlignXY ?? "X";

                if (string.IsNullOrEmpty(alignDirection))
                    return;

                // 第2步：框选要对齐的块参照（支持改变方向）
                var blockReferenceIds = new List<ObjectId>();

                PromptPointResult ppr;

                // 循环处理：允许用户先改变方向，再选择点
                while (true)
                {
                    // 每次循环都重新构造 PromptPointOptions，以确保提示中的默认方向为最新值
                    PromptPointOptions pointOptions = new PromptPointOptions($"\n请选择第一个角点 [水平(X)/垂直(Y)]<{alignDirection}>")
                    {
                        AppendKeywordsToMessage = false,
                        AllowNone = false
                    };
                    // 设置关键字（必须每次添加，因为新对象）
                    pointOptions.Keywords.Add("X", "X");
                    pointOptions.Keywords.Add("Y", "Y");

                    ppr = ed.GetPoint(pointOptions);

                    if (ppr.Status == PromptStatus.Keyword)
                    {
                        alignDirection = ppr.StringResult.ToUpper();
                        MyPlugin.CurrentUserData.AlignXY = alignDirection;
                        continue;
                    }

                    if (ppr.Status != PromptStatus.OK)
                        return;

                    break;
                }

                var startPointWcs = ppr.Value.TransformBy(ucs2wcs);

                db.LoadSysLineType(SystemLinetype.DASHED);

                // 创建用于Jig的临时矩形
                Polyline polyLine = new Polyline
                {
                    Closed = true,
                    Linetype = SystemLinetype.DASHED.ToString(),
                    Transparency = new Transparency(128),
                    ColorIndex = 31,
                    LinetypeScale = 1000 / db.Ltscale
                };
                for (int i = 0; i < 4; i++)
                {
                    polyLine.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);
                }

                try
                {
                    // 执行Jig选择
                    UcsSelectJig ucsSelectJig = new UcsSelectJig(startPointWcs, polyLine);
                    PromptResult dragResult = ed.Drag(ucsSelectJig);
                    if (dragResult.Status != PromptStatus.OK)
                        return;

                    var endPointWcs = ucsSelectJig.EndPointWcs;
                    Point3dCollection rectPoints = GetRectPointsInWcs(startPointWcs, endPointWcs, ucs2wcs);

                    // 过滤块参照
                    TypedValueList typedValues = new TypedValueList { typeof(BlockReference) };
                    SelectionFilter selectionFilter = new SelectionFilter(typedValues);

                    PromptSelectionResult promptSelectionResult = ed.SelectCrossingPolygon(rectPoints, selectionFilter);
                    if (promptSelectionResult.Status != PromptStatus.OK)
                        return;

                    // 验证并添加有效的块参照ObjectId
                    using (Transaction transaction = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in promptSelectionResult.Value.GetObjectIds())
                        {
                            BlockReference blockReference =
                                transaction.GetObject(id, OpenMode.ForRead) as BlockReference;

                            if (blockReference == null || blockReference.GetConnectionPoints().Count == 0)
                                continue;

                            LayerTableRecord layerTableRecord =
                                transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as LayerTableRecord;

                            if (layerTableRecord?.IsLocked == true)
                                continue;

                            blockReferenceIds.Add(id);
                        }
                    }

                    if (blockReferenceIds.Count == 0)
                    {
                        ed.WriteMessage("\n错误：没有选择到可对齐的块。");
                        return;
                    }

                    ed.WriteMessage($"\n已框选 {blockReferenceIds.Count} 个块。");
                    ed.WriteMessage($"\n当前对齐方向: [{(alignDirection == "X" ? "水平X" : "垂直Y")}]");

                    // 第3步：选择基准块参照
                    typedValues = new TypedValueList { typeof(BlockReference) };
                    selectionFilter = new SelectionFilter(typedValues);

                    var options = new PromptSelectionOptions
                    {
                        SingleOnly = true,
                        RejectObjectsOnLockedLayers = true,
                        MessageForAdding = "\n请选择对齐的基准块: "
                    };

                    PromptSelectionResult psr = ed.GetSelection(options, selectionFilter);

                    if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
                    {
                        ed.WriteMessage("\n错误：未选择基准块。");
                        return;
                    }

                    // 第4步：执行对齐操作
                    using (Transaction transaction = db.TransactionManager.StartTransaction())
                    {
                        var baseBlockId = psr.Value.GetObjectIds().FirstOrDefault();
                        BlockReference baseBlockReference = transaction.GetObject(
                            baseBlockId, OpenMode.ForRead) as BlockReference;
                        Point3d basePoint = baseBlockReference.Position;
                        Point3d ucsBasePoint = basePoint.TransformBy(ucs2wcs.Inverse());

                        int alignedCount = 0;

                        foreach (ObjectId blockRefId in blockReferenceIds)
                        {
                            BlockReference reference = transaction.GetObject(
                                blockRefId, OpenMode.ForWrite) as BlockReference;

                            if (reference == null)
                                continue;

                            try
                            {
                                Point3d blockReferenceUcsPoint = reference.Position.TransformBy(ucs2wcs.Inverse());
                                Point3d targetPoint;

                                if (alignDirection == "X")
                                {
                                    // 水平对齐：改变Y坐标
                                    targetPoint = new Point3d(
                                        blockReferenceUcsPoint.X,
                                        ucsBasePoint.Y,
                                        blockReferenceUcsPoint.Z);
                                }
                                else
                                {
                                    // 垂直对齐：改变X坐标
                                    targetPoint = new Point3d(
                                        ucsBasePoint.X,
                                        blockReferenceUcsPoint.Y,
                                        blockReferenceUcsPoint.Z);
                                }

                                // 转换回WCS并计算位移
                                Point3d targetPointWcs = targetPoint.TransformBy(ucs2wcs);
                                Vector3d displacement = reference.Position.GetVectorTo(targetPointWcs);
                                Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                                reference.TransformBy(transformMatrix);
                                alignedCount++;
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n警告：块对齐失败 - {ex.Message}");
                            }
                        }

                        if (alignedCount > 0)
                            transaction.Commit();

                        ed.WriteMessage($"\n✓ 对齐完成，成功对齐 {alignedCount} 个块。");
                    }
                }
                finally
                {
                    polyLine?.Dispose();
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage($"\n错误：{ex.Message}");
            }
        }

        #endregion
    }
}
