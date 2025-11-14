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
        [CommandMethod("F5_EquipmentAngle")]
        public void F5_EquipmentAngle()
        {
            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            string s1 = "\n作用：多个对象在ucs坐标系下，设置对象的旋转角度";
            string s2 = "\n操作方法：框选对象，设置旋转角度（默认ucs的x轴为0度，逆时针选择为正）";
            string s3 = "\n注意事项：";
            editor.WriteMessage(s1 + s2 + s3);

            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();

            Point3d startPoint3D = new Point3d();
            Point3d endPoint3D = new Point3d();

            PromptPointOptions promptPointOptions = new PromptPointOptions($"\n 请选择第一个角点:");
            PromptPointResult promptPointResult = editor.GetPoint(promptPointOptions);

            if (promptPointResult.Status == PromptStatus.OK)
            {
                startPoint3D = promptPointResult.Value;
            }
            else
            {
                transaction.Abort();
                return;
            }

            database.LoadSysLineType(SystemLinetype.DASHED);
            // 初始化矩形
            Polyline polyLine = new Polyline();
            for (int i = 0; i < 4; i++)
            {
                polyLine.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);
            }
            polyLine.Closed = true;
            polyLine.Linetype = "DASHED";
            polyLine.Transparency = new Transparency(128);
            polyLine.ColorIndex = 31;
            polyLine.LinetypeScale = 1000 / database.Ltscale;

            UcsSelectJig ucsSelectJig = new UcsSelectJig(startPoint3D, polyLine);
            PromptResult promptResult = editor.Drag(ucsSelectJig);
            if (promptResult.Status == PromptStatus.OK)
            {
                endPoint3D = ucsSelectJig.EndPointWcs.TransformBy(ucsToWcsMatrix3d.Inverse());
            }
            else
            {
                transaction.Abort();
                return;
            }

            Point3dCollection point3DCollection = GetPoint3DCollection(startPoint3D, endPoint3D, ucsToWcsMatrix3d);

            TypedValueList typedValues = new TypedValueList
            {
                typeof(BlockReference)
            };
            SelectionFilter selectionFilter = new SelectionFilter(typedValues);
            PromptSelectionResult promptSelectionResult = editor.SelectCrossingPolygon(point3DCollection, selectionFilter);
            List<BlockReference> blockReferences = new List<BlockReference>();
            SelectionSet selectionSet = promptSelectionResult.Value;
            if (selectionSet == null)
            {
                transaction.Abort();
                return;
            }
            foreach (var id in selectionSet.GetObjectIds())
            {
                BlockReference blockReference = transaction.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (blockReference == null) continue;
                LayerTableRecord layerTableRecord = transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layerTableRecord.IsLocked == true) continue;
                blockReferences.Add(blockReference);
            }
            foreach (BlockReference blockReference in blockReferences)
            {
                blockReference.UpgradeOpen();
                blockReference.TransformBy(ucsToWcsMatrix3d.Inverse());
                blockReference.Rotation = 0;
                blockReference.TransformBy(ucsToWcsMatrix3d);
                blockReference.DowngradeOpen();
            }
            transaction.Commit();
        }
        #endregion
    }
}
