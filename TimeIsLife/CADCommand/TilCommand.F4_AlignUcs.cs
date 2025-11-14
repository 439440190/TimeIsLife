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
		[CommandMethod("F4_AlignUcs")]
		public void F4_AlignUcs()
		{
			Document document = Application.DocumentManager.MdiActiveDocument;
			Database database = document.Database;
			Editor editor = document.Editor;
			Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

			string s1 = "\n作用：多个对象在ucs坐标系下，沿x轴或者y轴对齐";
			string s2 = "\n操作方法：框选对象，设置对齐方向（默认ucs的x轴），选择基准对齐对象";
			string s3 = "\n注意事项：";
			editor.WriteMessage(s1 + s2 + s3);

            using Transaction transaction = database.TransactionManager.StartTransaction();
            // 添加用户输入来选择连线类型
            PromptKeywordOptions keywordOptions = new PromptKeywordOptions("\n [水平(X)/垂直(Y)]: ");
            keywordOptions.Keywords.Add("X");
            keywordOptions.Keywords.Add("Y");
            keywordOptions.Keywords.Default = MyPlugin.CurrentUserData.AlignXY;

            PromptResult keywordResult = editor.GetKeywords(keywordOptions);
            if (keywordResult.Status != PromptStatus.OK) return;
            MyPlugin.CurrentUserData.AlignXY = keywordResult.StringResult;

            PromptPointResult ppr = editor.GetPoint(new PromptPointOptions("\n 请选择第一个角点："));

			if (ppr.Status != PromptStatus.OK) return;
			var startPoint3D = ppr.Value;

			database.LoadSysLineType(SystemLinetype.DASHED);

			// 初始化矩形
			Polyline polyLine = new Polyline
			{
				Closed = true,
				Linetype = SystemLinetype.DASHED.ToString(),
				Transparency = new Transparency(128),
				ColorIndex = 31,
				LinetypeScale = 1000 / database.Ltscale
			};
			for (int i = 0; i < 4; i++)
			{
				polyLine.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);
			}

			UcsSelectJig ucsSelectJig = new UcsSelectJig(startPoint3D, polyLine);
			PromptResult promptResult = editor.Drag(ucsSelectJig);
			if (promptResult.Status != PromptStatus.OK) return;

			var endPoint3D = ucsSelectJig.EndPointWcs.TransformBy(ucsToWcsMatrix3d.Inverse());

			Point3dCollection point3DCollection = GetPoint3DCollection(startPoint3D, endPoint3D, ucsToWcsMatrix3d);
			TypedValueList typedValues = new TypedValueList
				{
					typeof(BlockReference),
				};
			List<BlockReference> blockReferences = new List<BlockReference>();

			SelectionFilter selectionFilter = new SelectionFilter(typedValues);
			PromptSelectionResult promptSelectionResult =
				editor.SelectCrossingPolygon(point3DCollection, selectionFilter);
			if (promptSelectionResult.Status != PromptStatus.OK) return;

			foreach (var id in promptSelectionResult.Value.GetObjectIds())
			{
				BlockReference blockReference = transaction.GetObject(id, OpenMode.ForRead) as BlockReference;
				if (blockReference == null || blockReference.GetConnectionPoints().Count == 0) continue;
				LayerTableRecord layerTableRecord =
					transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as LayerTableRecord;
				if (layerTableRecord != null && layerTableRecord.IsLocked) continue;
				blockReferences.Add(blockReference);
			}

			//选择基准对齐块参照
			Point3d basePoint = new Point3d();
            PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions()
            {
                SingleOnly = true,
                RejectObjectsOnLockedLayers = true,
                MessageForAdding = "\n 请选择对齐的基准图元："
            };

            PromptSelectionResult psr = editor.GetSelection(promptSelectionOptions, selectionFilter);

            if (psr.Status != PromptStatus.OK) return;
            if (psr.Value.Count == 0) return;

			BlockReference baseBlockReference = transaction.GetObject(psr.Value.GetObjectIds().First(), OpenMode.ForRead) as BlockReference;
            if (baseBlockReference == null) return;
			basePoint = baseBlockReference.Position;

			foreach (BlockReference reference in blockReferences)
            {
                reference.UpgradeOpen();
                Point3d ucsBasePoint = basePoint.TransformBy(ucsToWcsMatrix3d.Inverse());
                if (keywordResult.StringResult == "X")
                {
                    Point3d blockReferenceUcsPoint = reference.Position.TransformBy(ucsToWcsMatrix3d.Inverse());
                    Vector3d vector3D = reference.Position.GetVectorTo(new Point3d(blockReferenceUcsPoint.X, ucsBasePoint.Y, blockReferenceUcsPoint.Z).TransformBy(ucsToWcsMatrix3d));
                    Matrix3d displacement = Matrix3d.Displacement(vector3D);
                    reference.TransformBy(displacement);
                }
                else
                {
                    Point3d blockReferenceUcsPoint = reference.Position.TransformBy(ucsToWcsMatrix3d.Inverse());
                    Vector3d vector3D = reference.Position.GetVectorTo(new Point3d(ucsBasePoint.X, blockReferenceUcsPoint.Y, blockReferenceUcsPoint.Z).TransformBy(ucsToWcsMatrix3d));
                    Matrix3d displacement = Matrix3d.Displacement(vector3D);
                    reference.TransformBy(displacement);
                }

                reference.DowngradeOpen();
            }
            transaction.Commit();
        }
		#endregion
	}
}
