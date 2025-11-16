using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using System.Linq;

using TimeIsLife.Helper;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        /// <summary>
        /// 命令方法，用于在两个块之间绘制一条连接线。
        /// </summary>
        /// <remarks>
        /// 此方法允许用户在图纸中选择两个块。每个块必须包含一个连接点（POINT）。
        /// 程序会在两个块的连接点之间绘制一条直线。
        /// </remarks>
        [CommandMethod("F1_ConnectSingleLine", CommandFlags.Modal)]
        public void F1_ConnectSingleLine()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n作用：两个块之间连线。");
            ed.WriteMessage("\n操作方法：依次点选两个块；块内必须包含连接点（POINT）。");

            while (true)
            {
                // 选择第一个块
                var br1 = SelectBlock("\n选择第一个块：", ed, db);
                if (br1 == null) return;

                br1.Highlight(); // 高亮显示第一个块
                // 选择第二个块
                var br2 = SelectBlock("\n选择第二个块：", ed, db);
                br1.Unhighlight(); // 取消高亮显示第一个块
                if (br2 == null) return;

                // 构建连接线
                var line = BuildConnectLine(br1, br2);
                if (line == null)
                {
                    ed.WriteMessage("\n至少有一个块不含连接点，无法连线。");
                    continue;
                }

                // 将连接线添加到当前空间
                using var tr = db.TransactionManager.StartTransaction();
                var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                ms.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }
        }

        /// <summary>
        /// 提示用户从图纸中选择一个块引用。
        /// </summary>
        /// <param name="msg">在命令行提示中显示的消息。</param>
        /// <param name="ed">用于与命令行交互的编辑器实例。</param>
        /// <param name="db">表示当前图纸的数据库实例。</param>
        /// <returns>
        /// 如果选择成功，则返回选中的 <see cref="BlockReference"/>；否则返回 <c>null</c>。
        /// </returns>
        private BlockReference SelectBlock(string msg, Editor ed, Database db)
        {
            var opt = new PromptSelectionOptions
            {
                MessageForAdding = msg, // 提示消息
                SingleOnly = true, // 仅允许单个选择
                RejectObjectsOnLockedLayers = true // 拒绝选择锁定图层上的对象
            };

            // 设置选择过滤器，仅允许选择块引用
            var filter = new SelectionFilter(
                new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Start, "INSERT")
                });

            // 获取用户选择的结果
            var res = ed.GetSelection(opt, filter);
            if (res.Status != PromptStatus.OK) return null;

            // 开启事务以读取选中的块引用
            using var tr = db.TransactionManager.StartTransaction();
            var id = res.Value.GetObjectIds().First();
            return tr.GetObject(id, OpenMode.ForRead) as BlockReference;
        }


    }
}
