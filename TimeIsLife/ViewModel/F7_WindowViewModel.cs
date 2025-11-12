using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dapper;

using DotNetARX;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using TimeIsLife.Helper;
using TimeIsLife.Jig;
using TimeIsLife.Model;
using TimeIsLife.View;

using static TimeIsLife.CADCommand.FireAlarmCommand1;

using Database = Autodesk.AutoCAD.DatabaseServices.Database;

namespace TimeIsLife.ViewModel
{
    internal class F7_WindowViewModel : ObservableObject
    {
        public static F7_WindowViewModel Instance { get; set; }
        public F7_WindowViewModel()
        {
            Initialize();
            GetFloorAreaLayerNameCommand = new RelayCommand(GetFloorAreaLayerName);
            GetFireAreaLayerNameCommand = new RelayCommand(GetFireAreaLayerName);
            GetRoomAreaLayerNameCommand = new RelayCommand(GetRoomAreaLayerName);
            GetAvoidanceAreaLayerNameCommand = new RelayCommand(GetAvoidanceAreaLayerName);
            GetEquipmentLayerNameCommand = new RelayCommand(GetEquipmentLayerName);
            GetWireLayerNameCommand = new RelayCommand(GetWireLayerName);
            GetYdbFileNameCommand = new RelayCommand(GetYdbFileName);
            GetBasePointCommand = new RelayCommand(GetBasePoint);
            GetFloorAreaCommand = new RelayCommand(GetFloorArea);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(Cancel);
        }
        private void Initialize()
        {
            Instance = this;
            AreaFloors = new ObservableCollection<AreaFloor>();
            Areas = new ObservableCollection<Area>();
        }

        #region 属性
        //楼层区域
        private string floorAreaLayerName;
        public string FloorAreaLayerName 
        {
            get => floorAreaLayerName;
            set => SetProperty(ref floorAreaLayerName, value);
        }
        //防火分区
        private string fireAreaLayerName;
        public string FireAreaLayerName
        {
            get => fireAreaLayerName;
            set => SetProperty(ref fireAreaLayerName, value);
        }

        //房间区域
        private string roomAreaLayerName;
        public string RoomAreaLayerName
        {
            get => roomAreaLayerName;
            set => SetProperty(ref roomAreaLayerName, value);
        }

        //禁止布线区域
        private string avoidanceAreaLayerName;
        public string AvoidanceAreaLayerName
        {
            get => avoidanceAreaLayerName;
            set => SetProperty(ref avoidanceAreaLayerName, value);
        }

        //设备图层
        private string equipmentLayerName;
        public string EquipmentLayerName
        {
            get => equipmentLayerName;
            set => SetProperty(ref equipmentLayerName, value);
        }

        //设备图层
        private string wireLayerName;
        public string WireLayerName
        {
            get => wireLayerName;
            set => SetProperty(ref wireLayerName, value);
        }

        //YDB数据文件
        private string ydbFileName;
        public string YdbFileName
        {
            get => ydbFileName;
            set => SetProperty(ref ydbFileName, value);
        }

        private ObservableCollection<AreaFloor> areaFloors;
        public ObservableCollection<AreaFloor> AreaFloors
        {
            get => areaFloors;
            set => SetProperty(ref areaFloors, value);
        }


        private AreaFloor selectedAreaFloor;
        public AreaFloor SelectedAreaFloor
        {
            get => selectedAreaFloor;
            set => SetProperty(ref selectedAreaFloor, value);
        }

        private ObservableCollection<Area> areas;
        public ObservableCollection<Area> Areas
        {
            get => areas;
            set => SetProperty(ref areas, value);
        }

        private Point3d referenceBasePoint;
        public Point3d ReferenceBasePoint
        {
            get => referenceBasePoint;
            set => SetProperty(ref referenceBasePoint, value);
        }
        
        private int slabThickness;
        public int SlabThickness
        {
            get => slabThickness;
            set => SetProperty(ref slabThickness, value);
        }

        private bool sCircularConnectionSelected;
        public bool IsCircularConnectionSelected
        {
            get => sCircularConnectionSelected;
            set => SetProperty(ref sCircularConnectionSelected, value);
        }

        private Vector3d baseVector;
        public Vector3d BaseVector
        {
            get => baseVector;
            set => SetProperty(ref baseVector, value);
        }
        #endregion

        #region 委托
        public IRelayCommand GetFloorAreaLayerNameCommand { get; }
        public IRelayCommand GetFireAreaLayerNameCommand { get; }
        public IRelayCommand GetRoomAreaLayerNameCommand { get; }
        public IRelayCommand GetAvoidanceAreaLayerNameCommand { get; }
        public IRelayCommand GetEquipmentLayerNameCommand { get; }
        public IRelayCommand GetWireLayerNameCommand { get; }
        public IRelayCommand GetYdbFileNameCommand { get; }
        public IRelayCommand GetBasePointCommand { get; }
        public IRelayCommand GetFloorAreaCommand { get; }
        public IRelayCommand ConfirmCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public bool Result { get; private set; }
        #endregion

        #region 方法
        private void GetFloorAreaLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择表示楼层区域的闭合多段线";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.FloorAreaLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetFireAreaLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择表示防火分区的闭合多段线";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.FireAreaLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetRoomAreaLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择表示房间区域的闭合多段线";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.RoomAreaLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetAvoidanceAreaLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择表示禁止布线区域的闭合多段线";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.AvoidanceAreaLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetEquipmentLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择设备图层";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.EquipmentLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetWireLayerName()
        {
            F7_Window.Instance.Hide();
            string message = @"选择线缆图层";
            string layerName = GetLayerName(message);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                F7_Window.Instance.ShowDialog();
                return;
            }
            Instance.WireLayerName = layerName;
            F7_Window.Instance.ShowDialog();
        }
        private void GetYdbFileName()
        {
            F7_Window.Instance.Hide();
            AreaFloors.Clear();

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
                YdbFileName = openFileDialog.FileName;
                LoadYdbDatabase();
                F7_Window.Instance.ShowDialog(); 
            }
            else
            {
                MessageBox.Show(@"请重新选择YDB文件！");
                F7_Window.Instance.ShowDialog();
            }         

            
        }
        public void LoadYdbDatabase()
        {
            using SQLiteConnection ydbConn = new SQLiteConnection($"Data Source={ydbFileName}");
            string sqlFloor = "SELECT f.ID,f.Name,f.LevelB,f.Height FROM tblFloor AS f WHERE f.Height != 0";
            IEnumerable<Floor> floors = ydbConn.Query<Floor>(sqlFloor);

            foreach (var floor in floors)
            {
                AreaFloor area = new AreaFloor
                {
                    Name = floor.Name,
                    Level = floor.LevelB
                };
                AreaFloors.Add(area);
            }
        }
        private void GetFloorArea() 
        {
            if (SelectedAreaFloor == null)
            {
                MessageBox.Show(@"未选择楼层！");
                return;
            }

            if (Instance.FloorAreaLayerName.IsNullOrWhiteSpace())
            {
                MessageBox.Show(@"请先完成楼层图层的选择！");
                return;
            }

            F7_Window.Instance.Hide();

            Document document = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            using Transaction transaction = database.TransactionManager.StartTransaction();
            try
            {
                //选择选项
                PromptEntityOptions promptEntityOptions = new PromptEntityOptions("\n选择表示楼层闭合多段线");
                PromptEntityResult result = editor.GetEntity(promptEntityOptions);
                if (result.Status != PromptStatus.OK)
                {
                    MessageBox.Show(@"重新选择楼层！");
                    return;
                }
                Polyline polyline = transaction.GetObject(result.ObjectId, OpenMode.ForRead) as Polyline;
                if (polyline == null)
                {
                    MessageBox.Show(@"选择对象不是多段线！");
                    return;
                }
                Instance.SelectedAreaFloor.X = polyline.GeometricExtents.MinPoint.X;
                Instance.SelectedAreaFloor.Y = polyline.GeometricExtents.MinPoint.Y;
                Instance.SelectedAreaFloor.Z = polyline.GeometricExtents.MinPoint.Z;
                //F7_WindowViewModel.instance.SelectedAreaFloor.MinPoint3d = polyline.GeometricExtents.MinPoint;
                //F7_WindowViewModel.instance.SelectedAreaFloor.MaxPoint3d = polyline.GeometricExtents.MaxPoint;

                //过滤器
                var typedValues = new TypedValueList
                {
                    { DxfCode.LayerName, this.FloorAreaLayerName },
                };

                SelectionSet selectionSet = editor.GetSelectionSet(SelectString.SelectWindowPolygon, null,
                    new SelectionFilter(typedValues), polyline.GetPoint3dCollection(ucsToWcsMatrix3d));
                if (selectionSet == null)
                {
                    MessageBox.Show(@"缺少楼层名称！");
                    return;
                }
                else if (selectionSet.Count != 1)
                {
                    MessageBox.Show(@"包含多个楼层名称！");
                    return;
                }

                var obj = transaction.GetObject(selectionSet.GetObjectIds()[0], OpenMode.ForRead);
                // 检查对象是否为DBText
                if (obj is DBText dbText)
                {
                    this.SelectedAreaFloor.Name = dbText.TextString;

                    // 这里可以对dbText进行更多操作
                }
                // 检查对象是否为MText
                else if (obj is MText mText)
                {
                    this.SelectedAreaFloor.Name = mText.Text;
                    // 这里可以对mText进行更多操作
                }
                else
                {
                    MessageBox.Show(@"未包含楼层名称！");
                    return;
                }
                Area area = new Model.Area
                {
                    Floor = Instance.SelectedAreaFloor,
                    Kind = 0,
                    VertexX = polyline.GetXValues(),
                    VertexY = polyline.GetYValues(),
                    VertexZ = polyline.GetZValues()
                };
                Instance.Areas.Add(area);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($@"发生错误: {ex.Message}");
                transaction.Abort();
            }
            finally
            {
                F7_Window.Instance.ShowDialog();
            }
        }
        private void GetBasePoint()
        {
            if (Instance.SelectedAreaFloor == null)
            {
                MessageBox.Show(@"未选择基点对应的楼层！");
                return;
            }

            F7_Window.Instance.Hide();

            Document document = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.CurrentDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                try
                {
                    List<Beam> beams;
                    List<Wall> walls;

                    using (var ydbConn = new SQLiteConnection($"Data Source={Instance.YdbFileName}"))
                    {
                        ydbConn.Open();

                        // 查询梁的逻辑
                        beams = QueryBeams(ydbConn, Instance.SelectedAreaFloor.Level);

                        // 查询墙的逻辑
                        walls = QueryWalls(ydbConn, Instance.SelectedAreaFloor.Level);

                        ydbConn.Close();
                    }

                    List<Polyline> polylines = GeneratePolylines(beams, walls);                    

                    BasePointJig basePointJig = new BasePointJig(polylines);
                    PromptResult promptResult = editor.Drag(basePointJig);
                    if (promptResult.Status == PromptStatus.OK)
                    {
                        Instance.ReferenceBasePoint = basePointJig._point;
                        BaseVector =
                            new Point3d(Instance.SelectedAreaFloor.X, Instance.SelectedAreaFloor.Y,
                                Instance.SelectedAreaFloor.Z).GetVectorTo(Instance.ReferenceBasePoint);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($@"发生错误: {ex.Message}");
                }
                finally
                {
                    transaction.Abort();
                }
            }
            F7_Window.Instance.ShowDialog();
        }
        private List<Beam> QueryBeams(SQLiteConnection ydbConn, double level)
        {
            //查询梁
            string sqlBeam = "SELECT b.ID,f.ID,f.LevelB,f.Height,bs.ID,bs.kind,bs.ShapeVal,bs.ShapeVal1,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                        "FROM tblBeamSeg AS b " +
                        "INNER JOIN tblFloor AS f on f.StdFlrID = b.StdFlrID " +
                        "INNER JOIN tblBeamSect AS bs on bs.ID = b.SectID " +
                        "INNER JOIN tblGrid AS g on g.ID = b.GridId " +
                        "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                        "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID " +
                        "WHERE f.LevelB = @LevelB"; // add a WHERE clause to filter by f.LevelB

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

            return ydbConn.Query(sqlBeam, mappingBeam,
                new { LevelB = level }).ToList();
        }
        private List<Wall> QueryWalls(SQLiteConnection ydbConn, double level)
        {
            //查询墙
            string sqlWall = "SELECT w.ID,f.ID,f.LevelB,f.Height,ws.ID,ws.kind,ws.B,g.ID,j1.ID,j1.X,j1.Y,j2.ID,j2.X,j2.Y " +
                "FROM tblWallSeg AS w " +
                "INNER JOIN tblFloor AS f on f.StdFlrID = w.StdFlrID " +
                "INNER JOIN tblWallSect AS ws on ws.ID = w.SectID " +
                "INNER JOIN tblGrid AS g on g.ID = w.GridId " +
                "INNER JOIN tblJoint AS j1 on g.Jt1ID = j1.ID " +
                "INNER JOIN tblJoint AS j2 on g.Jt2ID = j2.ID " +
                "WHERE f.LevelB = @LevelB";

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
            return ydbConn.Query(sqlWall, mappingWall,
                new { LevelB = level }).ToList();
        }
        private List<Polyline> GeneratePolylines(List<Beam> beams, List<Wall> walls)
        {
            List<Polyline> polylines = new List<Polyline>();

            #region 生成梁

            double startWidth = 0;
            double endWidth = 0;
            double height = 0;
            foreach (var beam in beams)
            {
                Point2d p1 = new Point2d(beam.Grid.Joint1.X, beam.Grid.Joint1.Y);
                Point2d p2 = new Point2d(beam.Grid.Joint2.X, beam.Grid.Joint2.Y);


                Polyline polyline = new Polyline();
                switch (beam.BeamSect.Kind)
                {
                    case 1:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                    case 2:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                    case 7:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                    case 13:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                    case 22:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                        height = Math.Min(double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]), double.Parse(beam.BeamSect.ShapeVal.Split(',')[4]));

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                    case 26:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[5]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[3]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;

                    default:
                        startWidth = endWidth = double.Parse(beam.BeamSect.ShapeVal.Split(',')[1]);
                        height = double.Parse(beam.BeamSect.ShapeVal.Split(',')[2]);

                        polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                        polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                        break;
                }
                polylines.Add(polyline);
            }
            #endregion

            #region 生成墙
            foreach (var wall in walls)
            {
                Point2d p1 = new Point2d(wall.Grid.Joint1.X, wall.Grid.Joint1.Y);
                Point2d p2 = new Point2d(wall.Grid.Joint2.X, wall.Grid.Joint2.Y);
                startWidth = wall.WallSect.B;
                endWidth = wall.WallSect.B;

                Polyline polyline = new Polyline();
                polyline.AddVertexAt(0, p1, 0, startWidth, endWidth);
                polyline.AddVertexAt(1, p2, 0, startWidth, endWidth);
                polylines.Add(polyline);
            }
            #endregion
            return polylines;
        }
        private void Confirm()
        {
            Result = true;
            F7_Window.Instance.Close();
        }
        private void Cancel()
        {
            Result = false;
            F7_Window.Instance.Close();
        }
        public void SaveState()
        {

            MyPlugin.CurrentUserData.FloorLayerName = this.FloorAreaLayerName;
            MyPlugin.CurrentUserData.FireAreaLayerName = this.FireAreaLayerName;
            MyPlugin.CurrentUserData.RoomLayerName = this.RoomAreaLayerName;
            MyPlugin.CurrentUserData.AvoidanceAreaLayerName = this.AvoidanceAreaLayerName;
            MyPlugin.CurrentUserData.EquipmentLayerName = this.EquipmentLayerName;
            MyPlugin.CurrentUserData.WireLayerName = WireLayerName;
            MyPlugin.CurrentUserData.YdbFileName = this.YdbFileName;
            MyPlugin.CurrentUserData.SlabThickness = this.SlabThickness;
        }
        public void LoadState()
        {
            FloorAreaLayerName = MyPlugin.CurrentUserData.FloorLayerName;
            FireAreaLayerName = MyPlugin.CurrentUserData.FireAreaLayerName;
            RoomAreaLayerName = MyPlugin.CurrentUserData.RoomLayerName;
            AvoidanceAreaLayerName = MyPlugin.CurrentUserData.AvoidanceAreaLayerName;
            EquipmentLayerName = MyPlugin.CurrentUserData.EquipmentLayerName;
            WireLayerName = MyPlugin.CurrentUserData.WireLayerName;
            YdbFileName = MyPlugin.CurrentUserData.YdbFileName;
            SlabThickness = MyPlugin.CurrentUserData.SlabThickness;
        }
        private string GetLayerName(string message)
        {
            Document document = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                //选择选项
                PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions
                {
                    SingleOnly = true,
                    RejectObjectsOnLockedLayers = true,
                    MessageForAdding = message
                };

                SelectionSet selectionSet = editor.GetSelectionSet(SelectString.GetSelection, promptSelectionOptions, null, null);
                if (selectionSet == null) return null;
                Entity entity = transaction.GetObject(selectionSet.GetObjectIds().FirstOrDefault(), OpenMode.ForRead) as Entity;
                if (entity == null)
                {
                    MessageBox.Show(@"对象图层锁定！");
                    return null;
                }
                return entity.Layer;
            }
        }
        #endregion
    }
}
