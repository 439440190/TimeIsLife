using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using System.Linq;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        /// <summary>
        /// 作用：根据选择对象的图层、颜色、线型、线型比例设置默认的图层、颜色、线型、线型比例
        /// 操作方法：运行命令，选择对象
        /// </summary>
        [CommandMethod("F2_SetCurrentStatus", CommandFlags.Modal)]
        public void F2_SetCurrentStatus()
        {
            // 获取当前文档和数据库的引用
            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            // 向用户说明此命令的作用和使用方法
            string s1 = "\n作用：根据选择对象的图层、颜色、线型、线型比例设置默认的图层、颜色、线型、线型比例。";
            string s2 = "\n操作方法：运行命令，选择对象。";
            editor.WriteMessage(s1 + s2);

            // 开启一个新的事务
            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            // 配置选择选项：单选，拒绝锁定图层上的对象
            PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions()
            {
                SingleOnly = true,
                RejectObjectsOnLockedLayers = true,
            };

            // 请求用户选择一个对象
            PromptSelectionResult promptSelectionResult = editor.GetSelection(promptSelectionOptions);

            // 确认选择成功
            if (promptSelectionResult.Status == PromptStatus.OK)
            {
                // 遍历选择集中的每个对象
                ObjectId objectId = promptSelectionResult.Value.GetObjectIds().FirstOrDefault();
                // 获取对象并转换为Entity类型以读取其属性
                Entity entity = transaction.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (entity == null) return;
                // 根据选择的实体设置当前绘图的默认属性
                database.Cecolor = entity.Color; // 设置默认颜色
                database.Clayer = entity.LayerId; // 设置默认图层
                database.Celtype = entity.LinetypeId; // 设置默认线型
                database.Celtscale = entity.LinetypeScale; // 设置默认线型比例
                //database.Celweight = entity.LineWeight; // 设置默认线宽
                //database.Cmaterial = entity.MaterialId; // 设置默认材质

                // 提交事务以应用更改
                transaction.Commit();
            }

        }
    }
}
