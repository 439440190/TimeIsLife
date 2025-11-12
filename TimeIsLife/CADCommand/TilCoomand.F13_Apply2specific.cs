using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using TimeIsLife.Helper;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCoomand
    {
        [CommandMethod("F13_Apply2specific", CommandFlags.Modal)]
        public void F13_Apply2specific()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            //string message = @"选择表示房间区域的闭合多段线";

            //using (Transaction transaction = database.TransactionManager.StartTransaction())
            //{
            //    //选择选项
            //    PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions
            //    {
            //        SingleOnly = true,
            //        RejectObjectsOnLockedLayers = true,
            //        MessageForAdding = message
            //    };

            //    SelectionSet selectionSet = editor.GetSelectionSet(SelectString.GetSelection, promptSelectionOptions, null, null);
            //    if (selectionSet == null) transaction.Abort(); ;
            //    Entity entity = transaction.GetObject(selectionSet.GetObjectIds().FirstOrDefault(), OpenMode.ForRead) as Entity;
            //    if (entity == null)
            //    {
            //        MessageBox.Show(@"对象图层锁定！");
            //        transaction.Abort();
            //    }
            //    Extents3d extents3D = entity.Bounds.Value;
            //    Zoom(extents3D.MinPoint, extents3D.MaxPoint);
            //    transaction.Commit();
            //}
        }

        private void Zoom(Point3d minPoint, Point3d maxPoint, Point3d pCenter, double dFactor)
        {
            // 获取当前文档和数据库
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;

            int nCurVport = System.Convert.ToInt32(Application.GetSystemVariable("CVPORT"));

            // 确定当前范围：如果pMin和pMax都为原点，则根据当前模式设置相应的范围
            if (minPoint == Point3d.Origin && maxPoint == Point3d.Origin)
            {
                if (database.TileMode) // 当前为模型空间
                {
                    minPoint = database.Extmin;
                    maxPoint = database.Extmax;
                }
                else // 当前为图纸空间
                {
                    minPoint = database.Pextmin;
                    maxPoint = database.Pextmax;
                }
            }

            // 开始事务
            using (Transaction acTrans = database.TransactionManager.StartTransaction())
            {
                // 获取当前视图
                using (ViewTableRecord currentView = document.Editor.GetCurrentView())
                {
                    // 将WCS坐标转换为DCS坐标的变换矩阵
                    Matrix3d matWCS2DCS = Matrix3d.PlaneToWorld(currentView.ViewDirection);
                    matWCS2DCS = Matrix3d.Displacement(currentView.Target - Point3d.Origin) * matWCS2DCS;
                    matWCS2DCS = Matrix3d.Rotation(-currentView.ViewTwist, currentView.ViewDirection, currentView.Target) * matWCS2DCS;

                    // 如果提供了中心点，则根据中心点设置范围
                    if (pCenter.DistanceTo(Point3d.Origin) != 0)
                    {
                        minPoint = new Point3d(pCenter.X - (currentView.Width / 2), pCenter.Y - (currentView.Height / 2), 0);
                        maxPoint = new Point3d((currentView.Width / 2) + pCenter.X, (currentView.Height / 2) + pCenter.Y, 0);
                    }

                    Extents3d eExtents = new Extents3d(minPoint, maxPoint);

                    // 计算当前视图的宽高比
                    double dViewRatio = currentView.Width / currentView.Height;

                    // 转换视图的范围
                    eExtents.TransformBy(matWCS2DCS.Inverse());

                    double dWidth, dHeight;
                    Point2d pNewCentPt;

                    if (pCenter.DistanceTo(Point3d.Origin) != 0)
                    {
                        dWidth = currentView.Width;
                        dHeight = currentView.Height;

                        if (dFactor == 0)
                        {
                            pCenter = pCenter.TransformBy(matWCS2DCS.Inverse());
                        }

                        pNewCentPt = new Point2d(pCenter.X, pCenter.Y);
                    }
                    else
                    {
                        dWidth = eExtents.MaxPoint.X - eExtents.MinPoint.X;
                        dHeight = eExtents.MaxPoint.Y - eExtents.MinPoint.Y;

                        pNewCentPt = new Point2d(
                            (eExtents.MaxPoint.X + eExtents.MinPoint.X) * 0.5,
                            (eExtents.MaxPoint.Y + eExtents.MinPoint.Y) * 0.5);
                    }

                    if (dWidth > dHeight * dViewRatio)
                    {
                        dHeight = dWidth / dViewRatio;
                    }

                    // 如果 dFactor 为正数，则视图将放大或缩小到原视图的 dFactor 倍。
                    //如果 dFactor 为零，则表示保持当前视图比例不变，只调整视图的中心点。
                    //如果 dFactor 为负数，通常是不合法的，可能需要处理以避免错误。
                    if (dFactor != 0)
                    {
                        currentView.Height = dHeight * dFactor;
                        currentView.Width = dWidth * dFactor;
                    }

                    currentView.CenterPoint = pNewCentPt;

                    document.Editor.SetCurrentView(currentView);
                }

                acTrans.Commit();
            }
        }
        private void Zoom(Point3d pMin, Point3d pMax)
        {
            // 获取当前文档和数据库
            Document acDoc = Application.DocumentManager.MdiActiveDocument;
            Database acCurDb = acDoc.Database;

            // 开始事务处理
            using (Transaction acTrans = acCurDb.TransactionManager.StartTransaction())
            {
                // 获取当前视图
                using (ViewTableRecord acView = acDoc.Editor.GetCurrentView())
                {
                    // 将WCS坐标转换为DCS坐标的变换矩阵
                    Matrix3d matWCS2DCS = Matrix3d.PlaneToWorld(acView.ViewDirection);
                    matWCS2DCS = Matrix3d.Displacement(acView.Target - Point3d.Origin) * matWCS2DCS;
                    matWCS2DCS = Matrix3d.Rotation(-acView.ViewTwist, acView.ViewDirection, acView.Target) * matWCS2DCS;

                    // 创建范围对象
                    Extents3d eExtents = new Extents3d(pMin, pMax);

                    // 转换视图的范围：将范围从WCS转换到DCS
                    eExtents.TransformBy(matWCS2DCS.Inverse());

                    // 计算新的宽度和高度
                    double dWidth = eExtents.MaxPoint.X - eExtents.MinPoint.X;
                    double dHeight = eExtents.MaxPoint.Y - eExtents.MinPoint.Y;

                    // 计算当前视图的宽高比
                    double dViewRatio = acView.Width / acView.Height;

                    // 如果新宽度超过了当前窗口的宽高比，则调整高度
                    if (dWidth > dHeight * dViewRatio)
                    {
                        dHeight = dWidth / dViewRatio;
                    }

                    // 设置新的中心点为范围的中心
                    Point2d pNewCentPt = new Point2d(
                        (eExtents.MaxPoint.X + eExtents.MinPoint.X) * 0.5,
                        (eExtents.MaxPoint.Y + eExtents.MinPoint.Y) * 0.5);

                    // 调整视图的宽度和高度
                    acView.Height = dHeight;
                    acView.Width = dWidth;

                    // 设置视图的中心点
                    acView.CenterPoint = pNewCentPt;

                    // 应用新的视图设置
                    acDoc.Editor.SetCurrentView(acView);
                }

                // 提交事务
                acTrans.Commit();
            }
        }


    }
}
