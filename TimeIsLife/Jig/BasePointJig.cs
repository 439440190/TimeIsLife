﻿using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using static TimeIsLife.CADCommand.FireAlarmCommnad;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace TimeIsLife.Jig
{
    internal class BasePointJig : DrawJig
    {
        public Point3d _point;
        private List<Polyline> polylines;
        public BasePointJig(List<Polyline> polylines)
        {
            this.polylines = polylines;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            Document document = Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            Matrix3d matrix = editor.CurrentUserCoordinateSystem;

            JigPromptPointOptions promptOptions = new JigPromptPointOptions("\n选择对齐基点:");
            promptOptions.UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NoZeroResponseAccepted | UserInputControls.NoNegativeResponseAccepted;
            promptOptions.Cursor = CursorType.Crosshair;

            PromptPointResult result = prompts.AcquirePoint(promptOptions);
            Point3d tempPoint = result.Value;

            if (result.Status == PromptStatus.Cancel)
            {
                return SamplerStatus.Cancel;
            }

            if (_point != tempPoint)
            {
                _point = tempPoint;
                Point3d ucsPoint3d = _point.TransformBy(matrix.Inverse());
                Vector3d vector3D = ucsPoint3d.GetVectorTo(Point3d.Origin);
                Matrix3d matrix3D = Matrix3d.Displacement(vector3D);

                foreach (var polyline in polylines)
                {
                    polyline.TransformBy(matrix);
                    polyline.TransformBy(matrix3D);
                }

                return SamplerStatus.OK;
            }
            else
            {
                return SamplerStatus.NoChange;
            }
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            foreach (var polyline in polylines)
            {
                draw.Geometry.Draw(polyline);
            }
            return true;
        }
    }
}