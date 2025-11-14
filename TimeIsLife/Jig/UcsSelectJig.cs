using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace TimeIsLife.Jig
{
    class UcsSelectJig : DrawJig
    {
        private Point3d startWcs;
        private Polyline rect;
        public Point3d EndPointWcs { get; private set; }

        public UcsSelectJig(Point3d startPointWcs, Polyline rect)
        {
            startWcs = startPointWcs;
            this.rect = rect;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Matrix3d ucs2wcs = ed.CurrentUserCoordinateSystem;

            JigPromptPointOptions opts = new JigPromptPointOptions("\n请选择第二个角点：")
            {
                Cursor = CursorType.Crosshair,
                UseBasePoint = true,
                BasePoint = startWcs
            };
            PromptPointResult res = prompts.AcquirePoint(opts); // 获取用户输入点WCS
            if (res.Status == PromptStatus.Cancel)
                return SamplerStatus.Cancel;

            Point3d newPtWcs = res.Value;
            if (newPtWcs.IsEqualTo(EndPointWcs))
                return SamplerStatus.NoChange;

            EndPointWcs = newPtWcs;

            // 转到 UCS 计算矩形
            Point3d ucsStart = startWcs.TransformBy(ucs2wcs.Inverse());
            Point3d ucsEnd = EndPointWcs.TransformBy(ucs2wcs.Inverse());

            rect.Normal = Vector3d.ZAxis;
            rect.Elevation = 0.0;
            rect.SetPointAt(0, new Point2d(ucsStart.X, ucsStart.Y));
            rect.SetPointAt(1, new Point2d(ucsStart.X, ucsEnd.Y));
            rect.SetPointAt(2, new Point2d(ucsEnd.X, ucsEnd.Y));
            rect.SetPointAt(3, new Point2d(ucsEnd.X, ucsStart.Y));

            // 转回 WCS 用于显示
            rect.TransformBy(ucs2wcs);
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
        {
            draw.Geometry.Draw(rect);
            return true;
        }
        //Point3d _startPoint3d;
        //public Point3d endPoint3d;
        //Polyline _polyline;
        //public UcsSelectJig(Point3d startPoint3d, Polyline polyline)
        //{
        //    this._startPoint3d = startPoint3d;
        //    this._polyline = polyline;
        //}
        //protected override SamplerStatus Sampler(JigPrompts prompts)
        //{
        //    Document document = Application.DocumentManager.CurrentDocument;
        //    Editor editor = document.Editor;

        //    Matrix3d matrixd = editor.CurrentUserCoordinateSystem;
        //    JigPromptPointOptions options = new JigPromptPointOptions("\n请选择第二个角点：")
        //    {
        //        Cursor = CursorType.Crosshair,
        //        UserInputControls = UserInputControls.NoZeroResponseAccepted |
        //                            UserInputControls.Accept3dCoordinates |
        //                            UserInputControls.NoNegativeResponseAccepted,
        //        UseBasePoint = true
        //    };
        //    PromptPointResult result = prompts.AcquirePoint(options);
        //    if (result.Status == PromptStatus.Cancel)
        //    {
        //        return SamplerStatus.Cancel;
        //    }
        //    Point3d tempPoint3d = result.Value;
        //    if (tempPoint3d != endPoint3d)
        //    {
        //        endPoint3d = tempPoint3d;
        //        //将WCS点转化为UCS点
        //        Point3d uscEndPoint3d = endPoint3d.TransformBy(matrixd.Inverse());
        //        _polyline.Normal = Vector3d.ZAxis;
        //        _polyline.Elevation = 0.0;
        //        _polyline.SetPointAt(0, new Point2d(_startPoint3d.X, _startPoint3d.Y));
        //        _polyline.SetPointAt(1, new Point2d(_startPoint3d.X, uscEndPoint3d.Y));
        //        _polyline.SetPointAt(2, new Point2d(uscEndPoint3d.X, uscEndPoint3d.Y));
        //        _polyline.SetPointAt(3, new Point2d(uscEndPoint3d.X, _startPoint3d.Y));
        //        _polyline.TransformBy(matrixd);
        //        return SamplerStatus.OK;
        //    }
        //    else
        //    {
        //        return SamplerStatus.NoChange;
        //    }
        //}

        //protected override bool WorldDraw(WorldDraw draw)
        //{
        //    draw.Geometry.Draw(_polyline);
        //    return true;
        //}
    }
}
