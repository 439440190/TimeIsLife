using Accord.MachineLearning.Boosting;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using DotNetARX;

using NetTopologySuite.Algorithm;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Media3D;

using TimeIsLife.Helper;
using TimeIsLife.Model;
using TimeIsLife.View;
using TimeIsLife.ViewModel;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        [CommandMethod("F9_FAS")]
        public void F9_FAS()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            string s1 = "\n作用：生成火灾自动报警系统图";
            string s2 = "\n操作方法：选择表示防火分区多段线，选择一个基点放置系统图，基点为生成系统图区域左下角点";
            string s3 = "\n注意事项：防火分区多段线需要单独图层，防火分区多段线内需要文字标注防火分区编号，文字和多段线需要同一个图层";
            editor.WriteMessage(s1 + s2 + s3);

            

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                List<string> layerNames1 = new List<string>() { MyPlugin.CurrentUserData.FireAreaLayerName };
                LayerTable layerTable = transaction.GetObject(database.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (layerTable == null) return;
                if (!CheckAllLayers(layerTable, layerNames1))
                {
                    MessageBox.Show(@"请选择防火分区图层！");
                    F9_Window.Instance.ShowDialog();
                    if (F9_WindowViewModel.Instance.Result)
                    {
                        List<string> layerNames2 = new List<string>() { MyPlugin.CurrentUserData.FireAreaLayerName };
                        if (!CheckAllLayers(layerTable, layerNames2))
                        {
                            MessageBox.Show(@"防火分区图层设置错误！");
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                bool bo = true;
                while (bo)
                {
                    // 添加用户输入来选择连线类型
                    PromptKeywordOptions keywordOptions = new PromptKeywordOptions("\n选择连线类型 [环形(Circle)/树形(Tree)/选项(Options)]: ");
                    keywordOptions.Keywords.Add("Circle");
                    keywordOptions.Keywords.Add("Tree");
                    keywordOptions.Keywords.Add("Options");
                    keywordOptions.Keywords.Default = MyPlugin.CurrentUserData.TreeOrCircle;

                    PromptResult keywordResult = editor.GetKeywords(keywordOptions);
                    if (keywordResult.Status != PromptStatus.OK) return;
                    switch (keywordResult.StringResult)
                    {
                        case "Circle":
                            MyPlugin.CurrentUserData.TreeOrCircle = keywordResult.StringResult;
                            bo= false;
                            break;
                        case "Tree":
                            MyPlugin.CurrentUserData.TreeOrCircle = keywordResult.StringResult;
                            bo = false;
                            break;
                        case "Options":
                            F9_Window.Instance.ShowDialog();
                            if (F9_WindowViewModel.Instance.Result)
                            {
                                List<string> layerNames3 = new List<string>() { MyPlugin.CurrentUserData.FireAreaLayerName };
                                if (!CheckAllLayers(layerTable, layerNames3))
                                {
                                    editor.WriteMessage(@"图层设置有误！");
                                    return;
                                }
                            }
                            else
                            {
                                return;
                            }
                            break;
                    }
                }
                

                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                string blockDirectory = Path.Combine(assemblyDirectory, "Block", "FA");
                string schematicBlockDirectory = Path.Combine(assemblyDirectory, "SchematicBlock", "FA");

                PromptPointResult promptPointResult = editor.GetPoint(new PromptPointOptions("\n请选择系统图放置点："));
                if (promptPointResult.Status != PromptStatus.OK) return;
                var basePoint3d = promptPointResult.Value;

                //选择选定图层上的所有多段线
                TypedValueList typedValueList = new TypedValueList
                {
                    { DxfCode.LayerName, MyPlugin.CurrentUserData.FireAreaLayerName },
                    typeof(Polyline)
                };
                SelectionFilter layerSelectionFilter = new SelectionFilter(typedValueList);
                PromptSelectionResult promptSelectionResult = editor.SelectAll(layerSelectionFilter);
                if (promptSelectionResult.Status != PromptStatus.OK) return;

                List<FireArea> fireAreas = new List<FireArea>();
                foreach (var objectId in promptSelectionResult.Value.GetObjectIds())
                {
                    Polyline fireAreaPolyline = transaction.GetObject(objectId, OpenMode.ForRead) as Polyline;
                    if (fireAreaPolyline == null) continue;
                    if (!fireAreaPolyline.Closed)
                    {
                        fireAreaPolyline.Highlight();
                        return;
                    }

                    TypedValueList values2 = new TypedValueList
                    {
                        { DxfCode.LayerName, MyPlugin.CurrentUserData.FireAreaLayerName },
                        typeof(DBText)
                    };
                    SelectionFilter dbTextSelectionFilter = new SelectionFilter(values2);

                    PromptSelectionResult textPromptSelectionResult =
                        editor.SelectWindowPolygon(fireAreaPolyline.GetPoint3dCollection(), dbTextSelectionFilter);
                    if (textPromptSelectionResult.Status != PromptStatus.OK) return;
                    DBText dBText = transaction.GetObject(textPromptSelectionResult.Value.GetObjectIds().First(),
                        OpenMode.ForRead) as DBText;
                    if (dBText == null)
                    {
                        editor.WriteMessage(@"缺少防火分区编号！");
                        return;
                    }
                    fireAreas.Add(new FireArea() { Name = dBText.TextString, Polyline = fireAreaPolyline,DeviceType = MyPlugin.CurrentUserData.DeviceType});
                }

                // 计算所有分区中的最低和最高楼层权重
                int minFloorWeight = fireAreas.Min(fa => FloorToInt(fa.Device.Floor));
                int maxFloorWeight = fireAreas.Max(fa => FloorToInt(fa.Device.Floor));

                // 分区分组，填充缺少的楼层，并尝试找到每个分组中第一个非占位符的 FireArea 进行排序
                var groupedSortedAreas = fireAreas
                    .Select(fa =>
                    {
                        return new
                        {
                            FireArea = fa,
                            Partition = fa.Device.Partition,
                            FloorWeight = FloorToInt(fa.Device.Floor),
                            IsPlaceholder = fa.Name == "Placeholder" // 标记是否为占位符
                        };
                    })
                    .GroupBy(fa => fa.Partition)
                    .Select(g =>
                    {
                        var fullRangeFloors = Enumerable.Range(minFloorWeight, maxFloorWeight - minFloorWeight + 1)
                            .Select(weight => g.FirstOrDefault(fa => fa.FloorWeight == weight)?.FireArea)
                            .ToList();

                        // 替换null值为占位符FireArea
                        for (int i = 0; i < fullRangeFloors.Count; i++)
                        {
                            if (fullRangeFloors[i] == null)
                            {
                                fullRangeFloors[i] = new FireArea() { Name = "Placeholder" };
                            }
                        }

                        return fullRangeFloors;
                    })
                    .OrderBy(g =>
                    {
                        // 尝试找到每个分组中第一个非占位符的 FireArea，用于排序
                        var firstNonPlaceholder = g.FirstOrDefault(fa => fa.Name != "Placeholder");
                        return firstNonPlaceholder.Device.Partition;
                    })
                    .ToList();




                //循环防火分区
                List<FireArea> tempFireAreas = new List<FireArea>();


                for (int i = 0; i < groupedSortedAreas.Count; i++)
                {
                    for (int j = 0; j < groupedSortedAreas[i].Count; j++)
                    {
                        FireArea fireArea = groupedSortedAreas[i][j];
                        if (fireArea.Name == "Placeholder") continue;
                        if (tempFireAreas.Contains(fireArea)) continue;
                        tempFireAreas.Add(fireArea);
                        List<BlockReference> blockReferences = new List<BlockReference>();
                        Point3d layoutPoint3d = basePoint3d + new Vector3d(50000 * i, 2500 * j, 0);

                        Matrix3d displacement = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d));
                        TypedValueList typedValues3 = new TypedValueList
                        {
                            typeof(BlockReference)
                        };
                        SelectionFilter blockReferenceSelectionFilter = new SelectionFilter(typedValues3);
                        PromptSelectionResult selectWindowPolygon =
                            editor.SelectWindowPolygon(fireArea.Polyline.GetPoint3dCollection(),
                                blockReferenceSelectionFilter);
                        foreach (var objectId in selectWindowPolygon.Value.GetObjectIds())
                        {
                            BlockReference blockReference =
                                transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;
                            if (blockReference != null)
                            {
                                LayerTableRecord layerTableRecord =
                                    transaction.GetObject(blockReference.LayerId, OpenMode.ForRead) as LayerTableRecord;
                                if (layerTableRecord != null && layerTableRecord.IsLocked == false)
                                {
                                    blockReferences.Add(blockReference);
                                }
                            }
                        }

                        List<FireAlarmEquipment> equipments = new List<FireAlarmEquipment>();
                        foreach (var blockReference in blockReferences)
                        {
                            foreach (var fireAlarmEquipment in MyPlugin.CurrentUserData.FireAlarmEquipments)
                            {
                                if (blockReference.Name == Path.GetFileNameWithoutExtension(fireAlarmEquipment.BlockPath))
                                {
                                    equipments.Add(fireAlarmEquipment);
                                }
                            }
                        }

                        // 使用LINQ对equipments集合按EquipmentType进行排序和去重
                        var uniqueSortedEquipments = equipments
                            .OrderBy(e => e.EquipmentType) // 首先按EquipmentType排序
                            .GroupBy(e => e.EquipmentType) // 根据EquipmentType进行分组
                            .Select(g => g.First()) // 每组中选择第一个元素
                            .ToList(); // 转换为List

                        foreach (var fireAlarmEquipment in MyPlugin.CurrentUserData.FireAlarmEquipments)
                        {
                            FireAlarmEquipmentType fireAlarmEquipmentType = fireAlarmEquipment.EquipmentType;

                            string n = (from blockReference in blockReferences
                                        where blockReference.Name ==
                                              Path.GetFileNameWithoutExtension(fireAlarmEquipment.BlockPath)
                                        select blockReference).ToList().Count.ToString();

                            switch (fireAlarmEquipmentType)
                            {
                                case FireAlarmEquipmentType.Fa01:
                                    if (int.Parse(n) == 0) break;
                                    int n1 = 0, n2 = 0;
                                    //获取短路隔离器数量n1
                                    //获取接线端子箱数量n2
                                    string name1 = string.Empty;
                                    string name2 = string.Empty;
                                    foreach (var equipment in MyPlugin.CurrentUserData.FireAlarmEquipments)
                                    {
                                        if (equipment.EquipmentType == FireAlarmEquipmentType.Fa01)
                                        {
                                            name1 = Path.GetFileNameWithoutExtension(equipment.BlockPath);
                                        }

                                        if (equipment.EquipmentType == FireAlarmEquipmentType.Fa69)
                                        {
                                            name2 = Path.GetFileNameWithoutExtension(equipment.BlockPath);
                                        }
                                    }

                                    foreach (var blockReference in blockReferences)
                                    {


                                        if (blockReference.Name == name1)
                                        {

                                            foreach (ObjectId id in blockReference.AttributeCollection)
                                            {
                                                AttributeReference attref =
                                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                                if (attref != null)
                                                {
                                                    n2 += int.Parse(attref.TextString);
                                                }
                                            }

                                        }

                                        if (blockReference.Name == name2)
                                        {

                                            foreach (ObjectId id in blockReference.AttributeCollection)
                                            {
                                                AttributeReference attref =
                                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                                if (attref != null)
                                                {
                                                    n1 += int.Parse(attref.TextString);
                                                }
                                            }
                                        }
                                    }


                                    string file1 = Path.Combine(schematicBlockDirectory,
                                        MyPlugin.CurrentUserData.TreeOrCircle,
                                        Path.GetFileName(fireAlarmEquipment.BlockPath));
                                    if (File.Exists(file1))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file1, FileShare.Read, true, null);
                                        db.CloseInput(true);

                                        BlockTable blockTable =
                                            trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                        BlockTableRecord modelSpace =
                                            trans.GetObject(blockTable[BlockTableRecord.ModelSpace],
                                                OpenMode.ForRead) as BlockTableRecord;
                                        List<DBText> dBTexts = new List<DBText>();
                                        foreach (var item in modelSpace)
                                        {
                                            DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                            if (dBText != null)
                                            {
                                                dBTexts.Add(dBText);
                                            }

                                            BlockReference blockReference =
                                                trans.GetObject(item, OpenMode.ForRead) as BlockReference;
                                            if (blockReference != null && blockReference.Name.Equals(name2))
                                            {
                                                blockReference.UpgradeOpen();
                                                foreach (ObjectId id in blockReference.AttributeCollection)
                                                {
                                                    AttributeReference attref =
                                                        trans.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                                    if (attref != null)
                                                    {
                                                        attref.UpgradeOpen();
                                                        //设置属性值
                                                        attref.TextString = n1.ToString();

                                                        attref.DowngradeOpen();
                                                    }
                                                }

                                                blockReference.DowngradeOpen();
                                            }
                                        }

                                        dBTexts = dBTexts.OrderBy(d => d.Position.X).ToList();
                                        dBTexts[0].EditContent(fireArea.Name);
                                        dBTexts[1].EditContent(n2.ToString());
                                        trans.Commit();

                                        database.Insert(displacement, db, false);
                                    }

                                    break;
                                case FireAlarmEquipmentType.Fa02:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa03:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa04:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa05:
                                    if (int.Parse(n) == 0) break;
                                    string file2 = fireAlarmEquipment.SchematicBlockPath;
                                    if (File.Exists(file2))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file2, FileShare.Read, true, null);
                                        db.CloseInput(true);

                                        BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                        BlockTableRecord blockTableRecord =
                                            trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                                        List<DBText> dBTexts = new List<DBText>();
                                        foreach (var item in blockTableRecord)
                                        {
                                            DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                            if (dBText != null)
                                            {
                                                dBTexts.Add(dBText);
                                            }
                                        }

                                        dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                                        dBTexts[0].UpgradeOpen();
                                        dBTexts[0].TextString = n;
                                        dBTexts[0].DowngradeOpen();
                                        trans.Commit();

                                        database.Insert(displacement, db, false);
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa06:
                                    if (int.Parse(n) == 0) break;
                                    bool b1 = uniqueSortedEquipments.Any(e => e.EquipmentType == FireAlarmEquipmentType.Fa05);
                                    if (!b1)
                                    {
                                        string file3 = fireAlarmEquipment.SchematicBlockPath;
                                        if (File.Exists(file3))
                                        {
                                            using Database db = new Database(false, true);
                                            using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                            db.ReadDwgFile(file3, FileShare.Read, true, null);
                                            db.CloseInput(true);

                                            BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                            BlockTableRecord blockTableRecord =
                                                trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                                            List<DBText> dBTexts = new List<DBText>();
                                            foreach (var item in blockTableRecord)
                                            {
                                                DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                                if (dBText != null)
                                                {
                                                    dBTexts.Add(dBText);
                                                }
                                            }

                                            dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                                            dBTexts[0].UpgradeOpen();
                                            dBTexts[0].TextString = n;
                                            dBTexts[0].DowngradeOpen();
                                            trans.Commit();

                                            database.Insert(displacement, db, false);
                                        }
                                        Vector3d schematicWidthAdjustment =
                                            new Vector3d(1600, 0, 0);
                                        Matrix3d adjustmentDisplacement = Matrix3d.Displacement(schematicWidthAdjustment);

                                        // 合并位移矩阵，并更新displacement变量
                                        displacement *= adjustmentDisplacement;
                                    }
                                    else
                                    {
                                        string file4 = Path.Combine(schematicBlockDirectory,
                                            "WallAndCeilingMount",
                                            Path.GetFileName(fireAlarmEquipment.BlockPath));
                                        if (File.Exists(file4))
                                        {
                                            using Database db = new Database(false, true);
                                            using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                            db.ReadDwgFile(file4, FileShare.Read, true, null);
                                            db.CloseInput(true);

                                            BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                            BlockTableRecord blockTableRecord =
                                                trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                                            List<DBText> dBTexts = new List<DBText>();
                                            foreach (var item in blockTableRecord)
                                            {
                                                DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                                if (dBText != null)
                                                {
                                                    dBTexts.Add(dBText);
                                                }
                                            }

                                            dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                                            dBTexts[0].UpgradeOpen();
                                            dBTexts[0].TextString = n;
                                            dBTexts[0].DowngradeOpen();
                                            trans.Commit();

                                            database.Insert(displacement, db, false);
                                        }
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa07:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa08:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa09:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa10:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa11:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa12:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa13:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa14:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa15:
                                    if (int.Parse(n) == 0) break;
                                    string file5 = fireAlarmEquipment.SchematicBlockPath;
                                    if (File.Exists(file5))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file5, FileShare.Read, true, null);
                                        db.CloseInput(true);

                                        BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                        BlockTableRecord blockTableRecord =
                                            trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                                        List<DBText> dBTexts = new List<DBText>();
                                        foreach (var item in blockTableRecord)
                                        {
                                            DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                            if (dBText != null)
                                            {
                                                dBTexts.Add(dBText);
                                            }
                                        }

                                        dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                                        dBTexts[0].UpgradeOpen();
                                        dBTexts[0].TextString = n;
                                        dBTexts[0].DowngradeOpen();
                                        dBTexts[1].UpgradeOpen();
                                        dBTexts[1].TextString = (2 * int.Parse(n)).ToString();
                                        dBTexts[1].DowngradeOpen();
                                        trans.Commit();

                                        database.Insert(displacement, db, false);
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa16:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa17:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa18:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa19:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa20:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa21:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa22:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa23:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa24:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa25:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa26:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa27:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa28:
                                    if (int.Parse(n) == 0) break;
                                    int n3 = 0;
                                    string name3 = string.Empty;
                                    foreach (var equipment in MyPlugin.CurrentUserData.FireAlarmEquipments)
                                    {
                                        if (equipment.EquipmentType == FireAlarmEquipmentType.Fa28)
                                        {
                                            name3 = Path.GetFileNameWithoutExtension(equipment.BlockPath);
                                        }
                                    }

                                    foreach (var blockReference in blockReferences)
                                    {
                                        if (blockReference.Name == name3)
                                        {
                                            foreach (ObjectId id in blockReference.AttributeCollection)
                                            {
                                                AttributeReference attref =
                                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                                if (attref != null)
                                                {
                                                    n3 += int.Parse(attref.TextString);
                                                }
                                            }

                                        }
                                    }

                                    string file6 = fireAlarmEquipment.SchematicBlockPath;
                                    if (File.Exists(file6))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file6, FileShare.Read, true, null);
                                        db.CloseInput(true);

                                        BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                        BlockTableRecord modelSpace =
                                            trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                                                BlockTableRecord;
                                        List<DBText> dBTexts = new List<DBText>();
                                        foreach (var item in modelSpace)
                                        {
                                            DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                            if (dBText != null)
                                            {
                                                dBText.UpgradeOpen();
                                                dBText.TextString = n3.ToString();
                                                dBText.DowngradeOpen();
                                            }
                                        }
                                        trans.Commit();
                                        database.Insert(displacement, db, false);
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa29:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa30:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa31:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa32:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa33:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa34:
                                    if (int.Parse(n) == 0) break;
                                    //获取消防风机控制箱数量n4，消防风机数量n5
                                    int n4 = 0, n5 = 0;
                                    string name4 = string.Empty;
                                    foreach (var equipment in MyPlugin.CurrentUserData.FireAlarmEquipments)
                                    {
                                        if (equipment.EquipmentType == FireAlarmEquipmentType.Fa34)
                                        {
                                            name4 = Path.GetFileNameWithoutExtension(equipment.BlockPath);
                                        }
                                    }
                                    foreach (var item in blockReferences)
                                    {
                                        if (item.Name == name4)
                                        {
                                            n4++;
                                            item.UpgradeOpen();
                                            foreach (ObjectId id in item.AttributeCollection)
                                            {
                                                AttributeReference attref =
                                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                                if (attref != null)
                                                {
                                                    n5 += int.Parse(attref.TextString);
                                                }
                                            }

                                            item.DowngradeOpen();
                                        }
                                    }
                                    string file7 = fireAlarmEquipment.SchematicBlockPath;
                                    if (File.Exists(file7))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file7, FileShare.Read, true, null);
                                        db.CloseInput(true);

                                        BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                                        BlockTableRecord modelSpace =
                                            trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                                                BlockTableRecord;
                                        List<DBText> dBTexts = new List<DBText>();
                                        foreach (var item in modelSpace)
                                        {
                                            DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                                            if (dBText != null)
                                            {
                                                dBTexts.Add(dBText);
                                            }
                                        }

                                        dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                                        dBTexts[0].EditContent(n4.ToString());
                                        dBTexts[1].EditContent(n5.ToString());
                                        trans.Commit();

                                        database.Insert(displacement, db, false);
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa35:
                                    if (int.Parse(n) == 0) break;
                                    Matrix3d matrix3D1 =
                                        Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d))* Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa35))* Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa37))* Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa38));
                                    string file8 = fireAlarmEquipment.SchematicBlockPath;
                                    if (File.Exists(file8))
                                    {
                                        using Database db = new Database(false, true);
                                        using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                                        db.ReadDwgFile(file8, FileShare.Read, true, null);
                                        db.CloseInput(true);
                                        trans.Commit();
                                        database.Insert(matrix3D1, db, false);
                                    }
                                    break;
                                case FireAlarmEquipmentType.Fa37:
                                    if (int.Parse(n) == 0) break;
                                    Matrix3d matrix3D2 =
                                        Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d)) * Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa37));
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, matrix3D2, n);
                                    break;
                                case FireAlarmEquipmentType.Fa38:
                                    if (int.Parse(n) == 0) break;
                                    Matrix3d matrix3D3 =
                                        Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d)) * Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa37)) * Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa38));
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, matrix3D3, n);
                                    break;
                                case FireAlarmEquipmentType.Fa39:
                                    if (int.Parse(n) == 0) break;
                                    Matrix3d matrix3D4 =
                                        Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d)) * Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa37)) * Matrix3d.Displacement(-GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType.Fa38));
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, matrix3D4, n);
                                    break;
                                case FireAlarmEquipmentType.Fa40:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa41:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa42:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                                case FireAlarmEquipmentType.Fa43:
                                    AddFireAlarmEquipment(database, fireAlarmEquipment, displacement, n);
                                    break;
                            }

                            if (fireAlarmEquipment.EquipmentType == FireAlarmEquipmentType.Fa35 || fireAlarmEquipment.EquipmentType == FireAlarmEquipmentType.Fa36 || fireAlarmEquipment.EquipmentType== FireAlarmEquipmentType.Fa37|| fireAlarmEquipment.EquipmentType == FireAlarmEquipmentType.Fa38 || fireAlarmEquipment.EquipmentType == FireAlarmEquipmentType.Fa39)
                            {
                                continue;
                            }
                            bool b = uniqueSortedEquipments.Any(e => e.EquipmentType == fireAlarmEquipment.EquipmentType);
                            if (b)
                            {

                                // 为了根据设备的示意图宽度进行调整，创建另一个位移矩阵
                                Vector3d schematicWidthAdjustment =
                                    new Vector3d(fireAlarmEquipment.SchematicWidth, 0, 0);
                                Matrix3d adjustmentDisplacement = Matrix3d.Displacement(schematicWidthAdjustment);

                                // 合并位移矩阵，并更新displacement变量
                                displacement *= adjustmentDisplacement;
                            }
                            else
                            {
                                if (fireAlarmEquipment.IsPositionFixedOnSchematic)
                                {
                                    // 为了根据设备的示意图宽度进行调整，创建另一个位移矩阵
                                    Vector3d schematicWidthAdjustment =
                                        new Vector3d(fireAlarmEquipment.SchematicWidth, 0, 0);
                                    Matrix3d adjustmentDisplacement = Matrix3d.Displacement(schematicWidthAdjustment);

                                    // 合并位移矩阵，并更新displacement变量
                                    displacement *= adjustmentDisplacement;
                                }
                            }

                        }
                    }
                }
                
                transaction.Commit();
            }
        }

        private Vector3d GetFireAlarmEquipmentVector3d(FireAlarmEquipmentType fireAlarmfAlarmEquipmentType)
        {
            return new Vector3d(MyPlugin.CurrentUserData.FireAlarmEquipments.FirstOrDefault(e => e.EquipmentType == fireAlarmfAlarmEquipmentType)!.SchematicWidth, 0, 0);
        }
        private void AddFireAlarmEquipment(Database database, FireAlarmEquipment fireAlarmEquipment,
            Matrix3d displacement, string n)
        {
            if (int.Parse(n) == 0) return;
            string file = fireAlarmEquipment.SchematicBlockPath;
            if (File.Exists(file))
            {
                using Database db = new Database(false, true);
                using Transaction trans = db.TransactionManager.StartOpenCloseTransaction();
                db.ReadDwgFile(file, FileShare.Read, true, null);
                db.CloseInput(true);

                BlockTable blockTable = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord blockTableRecord =
                    trans.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                List<DBText> dBTexts = new List<DBText>();
                foreach (var item in blockTableRecord)
                {
                    DBText dBText = trans.GetObject(item, OpenMode.ForRead) as DBText;
                    if (dBText != null)
                    {
                        dBTexts.Add(dBText);
                    }
                }

                foreach (var dbText in dBTexts)
                {
                    dbText.UpgradeOpen();
                    dbText.TextString = n;
                    dbText.DowngradeOpen();
                }

                trans.Commit();

                database.Insert(displacement, db, false);
            }
        }

        private string IntToFloor(int weight)
        {
            if (weight == int.MaxValue) return "R";
            if (weight == 0) return "BJ";
            if (weight < 0) return "B" + (-weight).ToString();
            return weight.ToString();
        }
        private int FloorToInt(string floor)
        {
            // 这里复用之前定义的楼层权重逻辑
            if (floor.StartsWith("B"))
            {
                if (floor == "BJ") return 0;
                return -int.Parse(floor.TrimStart('B'));
            }
            else if (floor == "R")
            {
                return int.MaxValue;
            }

            return int.Parse(floor);
        }

        /// <summary>
        /// 添加接线端子箱
        /// </summary>
        /// <param name="database">当前文件数据库</param>
        /// <param name="tempPoint3d">插入基点</param>
        /// <param name="file">插入的DWG文件路径</param>
        /// <param name="area">防火分区编号</param>
        /// <param name="n1">短路隔离器数量</param>
        /// <param name="n2">接线端子箱数量</param>
        private void AddElement2500(Database database, Point3d tempPoint3d, string file, string area, string n1,
            string n2)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in modelSpace)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBTexts.Add(dBText);
                        }

                        BlockReference blockReference =
                            transaction.GetObject(item, OpenMode.ForRead) as BlockReference;
                        if (blockReference != null && blockReference.Name.Equals("FA-总线短路隔离器"))
                        {
                            blockReference.UpgradeOpen();
                            foreach (ObjectId id in blockReference.AttributeCollection)
                            {
                                AttributeReference attref =
                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                if (attref != null)
                                {
                                    attref.UpgradeOpen();
                                    //设置属性值
                                    attref.TextString = n1;

                                    attref.DowngradeOpen();
                                }
                            }

                            blockReference.DowngradeOpen();
                        }
                    }

                    dBTexts = dBTexts.OrderBy(d => d.Position.X).ToList();
                    dBTexts[0].EditContent(area);
                    dBTexts[1].EditContent(n2);
                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加壁装消防广播
        /// </summary>
        /// <param name="database"></param>
        /// <param name="editor"></param>
        /// <param name="directory"></param>
        /// <param name="tempPoint3d"></param>
        /// <param name="basePoint3d"></param>
        /// <param name="name"></param>
        /// <param name="n"></param>
        private void AddElement1600(Database database, Point3d tempPoint3d, string file, string n)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord blockTableRecord =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in blockTableRecord)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBTexts.Add(dBText);
                        }
                    }

                    dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                    dBTexts[0].UpgradeOpen();
                    dBTexts[0].TextString = n;
                    dBTexts[0].DowngradeOpen();
                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加没有模块或模块数量与设备数量一致的图元
        /// </summary>
        /// <param name="database"></param>
        /// <param name="editor"></param>
        /// <param name="directory"></param>
        /// <param name="tempPoint3d"></param>
        /// <param name="basePoint3d"></param>
        /// <param name="name"></param>
        /// <param name="n"></param>
        private void AddElement1(Database database, Point3d tempPoint3d, string file, string n)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord blockTableRecord =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in blockTableRecord)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBTexts.Add(dBText);
                        }
                    }

                    for (int i = 0; i < dBTexts.Count; i++)
                    {
                        dBTexts[i].UpgradeOpen();
                        dBTexts[i].TextString = n;
                        dBTexts[i].DowngradeOpen();
                    }

                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加防火卷帘
        /// </summary>
        /// <param name="database"></param>
        /// <param name="tempPoint3d"></param>
        /// <param name="file"></param>
        /// <param name="n"></param>
        private void AddElement2(Database database, Point3d tempPoint3d, string file, string n)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord blockTableRecord =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in blockTableRecord)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBTexts.Add(dBText);
                        }
                    }

                    dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                    dBTexts[0].UpgradeOpen();
                    dBTexts[0].TextString = n;
                    dBTexts[0].DowngradeOpen();
                    dBTexts[1].UpgradeOpen();
                    dBTexts[1].TextString = (2 * int.Parse(n)).ToString();
                    dBTexts[1].DowngradeOpen();
                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加液位显示器
        /// </summary>
        /// <param name="database"></param>
        /// <param name="layoutPoint3d"></param>
        /// <param name="file"></param>
        /// <param name="n"></param>
        private void AddElement3(Database database, Point3d layoutPoint3d, string file, string n)
        {
            Matrix3d matrix3D =
                Matrix3d.Displacement(Point3d.Origin.GetVectorTo(layoutPoint3d) + new Vector3d(-10000, 0, 0));

            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);
                    transaction.Commit();
                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加非消防强切点位
        /// </summary>
        /// <param name="database"></param>
        /// <param name="tempPoint3d"></param>
        /// <param name="file"></param>
        /// <param name="n">非消防强切点位</param>
        private void AddElement4(Database database, Point3d tempPoint3d, string file, string n)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in modelSpace)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBText.UpgradeOpen();
                            dBText.TextString = n;
                            dBText.DowngradeOpen();
                        }

                        BlockReference blockReference =
                            transaction.GetObject(item, OpenMode.ForRead) as BlockReference;
                        if (blockReference != null && blockReference.Name.Equals("FA-28-非消防配电箱"))
                        {
                            blockReference.UpgradeOpen();
                            foreach (ObjectId id in blockReference.AttributeCollection)
                            {
                                AttributeReference attref =
                                    transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                                if (attref != null)
                                {
                                    attref.UpgradeOpen();
                                    //设置属性值
                                    attref.TextString = n;

                                    attref.DowngradeOpen();
                                }
                            }

                            blockReference.DowngradeOpen();
                        }
                    }

                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }

        /// <summary>
        /// 添加消防风机控制箱
        /// </summary>
        /// <param name="database"></param>
        /// <param name="tempPoint3d"></param>
        /// <param name="file"></param>
        /// <param name="n4">消防风机控制箱数量</param>
        /// <param name="n5">消防风机数量</param>
        private void AddElement5(Database database, Point3d tempPoint3d, string file, string n4, string n5)
        {
            Matrix3d matrix3D = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(tempPoint3d));
            if (File.Exists(file))
            {

                using (Database db = new Database(false, true))
                using (Transaction transaction = db.TransactionManager.StartOpenCloseTransaction())
                {
                    db.ReadDwgFile(file, FileShare.Read, true, null);
                    db.CloseInput(true);

                    BlockTable blockTable = transaction.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace =
                        transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as
                            BlockTableRecord;
                    List<DBText> dBTexts = new List<DBText>();
                    foreach (var item in modelSpace)
                    {
                        DBText dBText = transaction.GetObject(item, OpenMode.ForRead) as DBText;
                        if (dBText != null)
                        {
                            dBTexts.Add(dBText);
                        }
                    }

                    dBTexts = dBTexts.OrderBy(d => d.Position.Y).ToList();
                    dBTexts[0].EditContent(n4);
                    dBTexts[1].EditContent(n5);
                    transaction.Commit();

                    database.Insert(matrix3D, db, false);
                }
            }
        }
    }
}
