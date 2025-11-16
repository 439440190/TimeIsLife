using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Dapper;


using DotNetARX;

using System;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TimeIsLife.NTSHelper;
using TimeIsLife.ViewModel;

using static TimeIsLife.CADCommand.FireAlarmCommand1;
using Color = Autodesk.AutoCAD.Colors.Color;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Database = Autodesk.AutoCAD.DatabaseServices.Database;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;


namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("F6_LoadYdbFile")]
        public void F6_LoadYdbFile()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            #region 选择盈建科文件
            string filename = null;
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = @"盈建科数据库(*.ydb)|*.ydb|所有文件|*.*",
                Title = @"选择结构模型数据库文件",
                ValidateNames = true,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                filename = openFileDialog.FileName;
            }
            else
            {
                return;
            }

            string directoryName = Path.GetDirectoryName(filename);
            editor.WriteMessage("\n文件名：" + filename);
            #endregion

            #region 数据查询
            var conn = new SQLiteConnection($"Data Source={filename}");

            //查询标高
            string sqlFloor = "SELECT f.ID,f.LevelB,f.Height FROM tblFloor AS f WHERE f.Height != 0";
            var floors = conn.Query<Floor>(sqlFloor);

            //查询梁
            string sqlBeam = "SELECT b.ID,f.ID,f.LevelB,f.Height,bs.ID,bs.kind,bs.ShapeVal,bs.ShapeVal1,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                "FROM tblBeamSeg AS b " +
                "INNER JOIN tblFloor AS f on f.StdFlrID = b.StdFlrID " +
                "INNER JOIN tblBeamSect AS bs on bs.ID = b.SectID " +
                "INNER JOIN tblGrid AS g on g.ID = b.GridId " +
                "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID";

            Func<Beam, Floor, BeamSect, Grid, Joint, Joint, Beam> mappingBeam =
                (beam, floor, beamSect, grid, j1, j2) =>
                {
                    grid.Joint1 = j1;
                    grid.Joint2 = j2;
                    beam.Grid = grid;
                    beam.Floor = floor;
                    beam.BeamSect = beamSect;
                    return beam;
                };

            var beams = conn.Query(sqlBeam, mappingBeam);

            //查询板
            string sqlSlab = "SELECT s.ID,s.GridsID,s.VertexX,s.VertexY,s.VertexZ,s.Thickness,f.ID,f.LevelB,f.Height FROM tblSlab  AS s INNER JOIN tblFloor AS f on f.StdFlrID = s.StdFlrID";
            Func<Slab, Floor, Slab> mappingSlab =
                (slab, floor) =>
                {
                    slab.Floor = floor;
                    return slab;
                };
            var slabs = conn.Query(sqlSlab, mappingSlab);

            //查询墙
            string sqlWall = "SELECT w.ID,f.ID,f.LevelB,f.Height,ws.ID,ws.kind,ws.B,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                "FROM tblWallSeg AS w " +
                "INNER JOIN tblFloor AS f on f.StdFlrID = w.StdFlrID " +
                "INNER JOIN tblWallSect AS ws on ws.ID = w.SectID " +
                "INNER JOIN tblGrid AS g on g.ID = w.GridId " +
                "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID";

            Func<Wall, Floor, WallSect, Grid, Joint, Joint, Wall> mappingWall =
                (wall, floor, wallSect, grid, j1, j2) =>
                {
                    grid.Joint1 = j1;
                    grid.Joint2 = j2;
                    wall.Grid = grid;
                    wall.Floor = floor;
                    wall.WallSect = wallSect;
                    return wall;
                };
            var walls = conn.Query(sqlWall, mappingWall);
            //关闭数据库
            conn.Close();

            #endregion

            using (Database blcokDatabase = new Database(false, true))
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                try
                {
                    string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                    UriBuilder uri = new UriBuilder(codeBase);
                    string path = Uri.UnescapeDataString(uri.Path);

                    //根据标高生成梁图
                    foreach (var floor in floors)
                    {
                        using (Database newDatabase = new Database())
                        using (Transaction newTransaction = newDatabase.TransactionManager.StartTransaction())
                        {
                            try
                            {

                                #region 生成板及烟感
                                foreach (var slab in slabs)
                                {
                                    
                                    if (slab.Floor.LevelB != floor.LevelB) continue;

                                    SetCurrentLayer(newDatabase, $"slab-{slab.Thickness.ToString()}mm", 7);

                                    Polyline polyline = new Polyline();
                                    Point2dCollection point2Ds = GetPoint2Ds(slab);
                                    if (point2Ds != null)
                                        if (polyline != null)
                                            polyline.CreatePolyline(point2Ds);
                                    newDatabase.AddToModelSpace(polyline);
                                }


                                #endregion

                                #region 生成梁
                                foreach (var beam in beams)
                                {
                                    if (beam.Floor.LevelB != floor.LevelB) continue;

                                    Point2d p1 = new Point2d(beam.Grid.Joint1.X, beam.Grid.Joint1.Y);
                                    Point2d p2 = new Point2d(beam.Grid.Joint2.X, beam.Grid.Joint2.Y);
                                    double startWidth = 0;
                                    double endWidth = 0;
                                    double height = 0;

                                    Polyline polyline = new Polyline();

                                    switch (beam.BeamSect.Kind)
                                    {
                                        case 1:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-concrete-{height.ToString()}mm", 7);
                                            break;
                                        case 2:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;
                                        case 7:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;
                                        case 13:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;
                                        case 22:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                                            height = Math.Min(double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]), double.Parse(beam.BeamSect.ShapeVal.Split(',')[4]));

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;
                                        case 26:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[5]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;

                                        default:
                                            startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                                            height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                                            polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                            polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                            SetCurrentLayer(newDatabase, $"beam-steel-{beam.BeamSect.Kind.ToString()}-{height.ToString()}mm", 7);
                                            break;
                                    }

                                    newDatabase.AddToModelSpace(polyline);
                                }
                                #endregion

                                #region 生成墙
                                foreach (var wall in walls)
                                {
                                    if (wall.Floor.LevelB != floor.LevelB) continue;

                                    SetCurrentLayer(newDatabase, "wall", 53);

                                    Point2d p1 = new Point2d(wall.Grid.Joint1.X, wall.Grid.Joint1.Y);
                                    Point2d p2 = new Point2d(wall.Grid.Joint2.X, wall.Grid.Joint2.Y);
                                    int startWidth = wall.WallSect.B;
                                    int endWidth = wall.WallSect.B;

                                    Polyline polyline = new Polyline();
                                    polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                                    polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                                    newDatabase.AddToModelSpace(polyline);
                                }
                                #endregion


                                string dwgName = Path.Combine(directoryName, floor.LevelB.ToString() + ".dwg");
                                newDatabase.SaveAs(dwgName, DwgVersion.Current);
                                newTransaction.Commit();
                                editor.WriteMessage($"\n{dwgName}");
                            }
                            catch
                            {
                                newTransaction.Abort();
                                editor.WriteMessage("\n发生错误1");
                            }
                        }
                    }
                    transaction.Commit();
                }
                catch
                {
                    transaction.Abort();
                    editor.WriteMessage("\n发生错误2");
                }
            }
            editor.WriteMessage("\n结束");
        }


        /// <summary>
        /// 根据已知点集合求重心
        /// </summary>
        /// <param name="mPoints">点集合</param>
        /// <returns>重心</returns>
        public Point2d getCenterOfGravityPoint(Point2dCollection mPoints)
        {
            double area = 0;//多边形面积  
            double Gx = 0, Gy = 0;// 重心的x、y  
            for (int i = 1; i <= mPoints.Count; i++)
            {
                double iLat = mPoints[i % mPoints.Count].X;
                double iLng = mPoints[i % mPoints.Count].Y;
                double nextLat = mPoints[(i - 1)].X;
                double nextLng = mPoints[(i - 1)].Y;
                double temp = (iLat * nextLng - iLng * nextLat) / 2;
                area += temp;
                Gx += temp * (iLat + nextLat) / 3;
                Gy += temp * (iLng + nextLng) / 3;
            }
            Gx = Gx / area;
            Gy = Gy / area;
            return new Point2d(Gx, Gy);
        }

        /// <summary>
        /// 获取板轮廓线点的集合
        /// </summary>
        /// <param name="slab">板</param>
        /// <returns>点集合</returns>
        private Point2dCollection GetPoint2Ds(Slab slab)
        {
            Point2dCollection points = new Point2dCollection();
            int n = slab.VertexX.Split(',').Length;
            if(n<= 1) { return points; }
            for (int i = 0; i < n; i++)
            {
                points.Add(new Point2d(double.Parse(slab.VertexX.Split(',')[i % (n - 1)]), double.Parse(slab.VertexY.Split(',')[i % (n - 1)])));
            }

            return points;
        }

        /// <summary>
        /// 获取YDB文件
        /// </summary>
        /// <returns>返回文件路径</returns>
        private string GetFilePath()
        {
            //List<string> list = new List<string>();
            string filename = null;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "盈建科数据库(*.ydb)|*.ydb|所有文件|*.*";
            openFileDialog.Title = "选择结构模型数据库文件";
            openFileDialog.ValidateNames = true;
            openFileDialog.CheckFileExists = true;
            openFileDialog.CheckPathExists = true;
            openFileDialog.Multiselect = false;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                filename = openFileDialog.FileName;
                //list.AddRange(dbPaths);
            }
            else
            {
                return null;
            }
            return filename;
        }
    }
}
