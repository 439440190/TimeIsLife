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
using System.Text;
using System.Threading.Tasks;
using TimeIsLife.Helper;
using TimeIsLife.Jig;

using TimeIsLife.Model;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {

        #region F5_EquipmentAngle

        /// <summary>
        /// 命令方法：在UCS坐标系下设置多个块参照的旋转角度
        /// </summary>
        /// <remarks>
        /// 操作流程：
        /// 1. 框选对象
        /// 2. 设置旋转角度（默认UCS的X轴为0度，逆时针为正）
        /// 3. 应用旋转变换
        /// </remarks>
        [CommandMethod("F5_EquipmentAngle")]
        public void F5_EquipmentAngle()
        {
            try
            {
                // 获取当前文档、数据库、编辑器和UCS-WCS变换矩阵
                var doc = Application.DocumentManager.MdiActiveDocument;
                var db = doc.Database;
                var ed = doc.Editor;
                var ucs2wcs = ed.CurrentUserCoordinateSystem;

                // 显示命令帮助信息
                ed.WriteMessage(
                    "\n作用：多个对象在UCS坐标系下设置旋转角度。" +
                    "\n操作方法：框选对象，输入旋转角度。" +
                    "\n坐标参考：默认UCS的X轴为0度，逆时针为正。\n");

                // ========================================
                // 第1步：框选要旋转的块参照
                // ========================================
                var blockReferenceIds = new List<ObjectId>();

                // 提示用户选择第一个角点
                PromptPointResult ppr = ed.GetPoint("\n请选择第一个角点: ");
                if (ppr.Status != PromptStatus.OK)
                    return;

                // 转换为WCS坐标
                var startPointWcs = ppr.Value.TransformBy(ucs2wcs);

                // 加载虚线线型
                db.LoadSysLineType(SystemLinetype.DASHED);

                // 创建用于Jig的临时矩形（用于框选可视化）
                Polyline polyLine = new Polyline
                {
                    Closed = true,                                  // 闭合
                    Linetype = SystemLinetype.DASHED.ToString(),    // 虚线
                    Transparency = new Transparency(128),           // 半透明
                    ColorIndex = 31,                                // 白色
                    LinetypeScale = 1000 / db.Ltscale              // 线型缩放
                };
                // 初始化4个顶点
                for (int i = 0; i < 4; i++)
                {
                    polyLine.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);
                }

                try
                {
                    // 执行Jig交互式选择（用户拖拽第二个角点）
                    UcsSelectJig ucsSelectJig = new UcsSelectJig(startPointWcs, polyLine);
                    PromptResult dragResult = ed.Drag(ucsSelectJig);
                    if (dragResult.Status != PromptStatus.OK)
                        return;

                    // 获取结束点
                    var endPointWcs = ucsSelectJig.EndPointWcs;
                    // 获取框选矩形的四个点
                    Point3dCollection rectPoints = GetRectPointsInWcs(startPointWcs, endPointWcs, ucs2wcs);

                    // 创建选择过滤器（只选择块参照）
                    TypedValueList typedValues = new TypedValueList { typeof(BlockReference) };
                    SelectionFilter selectionFilter = new SelectionFilter(typedValues);

                    // 执行框选多边形选择
                    PromptSelectionResult promptSelectionResult = ed.SelectCrossingPolygon(rectPoints, selectionFilter);
                    if (promptSelectionResult.Status != PromptStatus.OK)
                        return;

                    // 验证并收集有效的块参照ObjectId
                    using (Transaction transaction = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in promptSelectionResult.Value.GetObjectIds())
                        {
                            BlockReference blockReference =
                                transaction.GetObject(id, OpenMode.ForRead) as BlockReference;

                            // 跳过无效块
                            if (blockReference == null)
                                continue;

                            // 检查层是否锁定
                            LayerTableRecord layerTableRecord =
                                transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as LayerTableRecord;

                            if (layerTableRecord?.IsLocked == true)
                                continue;

                            // 添加有效的块ObjectId（注意：不添加块对象本身）
                            blockReferenceIds.Add(id);
                        }
                    }

                    // 检查是否选择了块
                    if (blockReferenceIds.Count == 0)
                    {
                        ed.WriteMessage("\n错误：没有选择到可旋转的块。");
                        return;
                    }

                    // 显示已选择块的数量
                    ed.WriteMessage($"\n已框选 {blockReferenceIds.Count} 个块。");

                    // ========================================
                    // 第2步：输入旋转角度
                    // ========================================
                    PromptDoubleOptions angleOptions = new PromptDoubleOptions("\n请输入旋转角度(度)");
                    angleOptions.AllowZero = true;              // 允许0度
                    angleOptions.AllowNegative = true;          // 允许负数（顺时针）
                    angleOptions.DefaultValue = 0;              // 默认值为0
                    angleOptions.UseDefaultValue = true;        // 使用默认值

                    PromptDoubleResult angleResult = ed.GetDouble(angleOptions);
                    if (angleResult.Status != PromptStatus.OK)
                        return;

                    // 获取输入的旋转角度（度）
                    double rotationAngle = angleResult.Value;
                    // 将度转换为弧度（AutoCAD使用弧度）
                    double rotationRadians = rotationAngle * Math.PI / 180.0;

                    ed.WriteMessage($"\n设置旋转角度: {rotationAngle}°");

                    // ========================================
                    // 第3步：执行旋转操作
                    // ========================================
                    using (Transaction transaction = db.TransactionManager.StartTransaction())
                    {
                        int rotatedCount = 0;

                        // 遍历所有选择的块参照
                        foreach (ObjectId blockRefId in blockReferenceIds)
                        {
                            // 在当前事务中重新获取块对象（升级为写权限）
                            BlockReference reference = transaction.GetObject(
                                blockRefId, OpenMode.ForWrite) as BlockReference;

                            if (reference == null)
                                continue;

                            try
                            {
                                // 在UCS坐标系下直接设置旋转角度（弧度）
                                reference.Rotation = rotationRadians;
                                rotatedCount++;
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n警告：块旋转失败 - {ex.Message}");
                            }
                        }

                        // 只有成功旋转块时才提交事务
                        if (rotatedCount > 0)
                            transaction.Commit();

                        // 显示旋转完成结果
                        ed.WriteMessage($"\n✓ 旋转完成，成功旋转 {rotatedCount} 个块。");
                    }
                }
                finally
                {
                    // 确保Polyline资源被释放
                    polyLine?.Dispose();
                }
            }
            catch (System.Exception ex)
            {
                // 捕获并显示所有异常
                Application.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage($"\n错误：{ex.Message}");
            }
        }

        #endregion
    }
}
