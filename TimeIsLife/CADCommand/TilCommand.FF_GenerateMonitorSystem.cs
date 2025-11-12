using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using DotNetARX;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        #region FF_GenerateMonitorSystem
        [CommandMethod("FF_GenerateMonitorSystem", CommandFlags.UsePickSet)]
        public void FF_GenerateMonitorSystem()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;

            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            BlockTable blockTable = transaction.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
            BlockTableRecord modelSpace = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
            Matrix3d matrixd = Application.DocumentManager.MdiActiveDocument.Editor.CurrentUserCoordinateSystem;

            string blockName1 = "E-配电箱-竖向干线";
            PromptPointOptions pointOptions = new PromptPointOptions("\n请选择起始点：");
            PromptPointResult pointResult = editor.GetPoint(pointOptions);
            if (pointResult.Status != PromptStatus.OK) return;
            Point3d point3D4 = pointResult.Value;


            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            string AssemblyDirectory = Path.GetDirectoryName(path);
            string directory = Path.Combine(AssemblyDirectory, "Block");

            List<BlockReference> blockReferences1 = new List<BlockReference>();
            PromptSelectionResult psr = editor.SelectImplied();
            if (psr.Status == PromptStatus.OK)
            {
                SelectionSet selectionSet = psr.Value;
                if (selectionSet.Count > 0)
                {
                    foreach (var objectId in selectionSet.GetObjectIds())
                    {
                        BlockReference blockReference = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;
                        if (blockReference == null || blockReference.Name != blockName1) continue;
                        blockReferences1.Add(blockReference);
                    }
                }
            }
            else
            {
                TypedValueList typedValues = new TypedValueList();
                typedValues.Add(typeof(BlockReference));
                SelectionFilter selectionFilter = new SelectionFilter(typedValues);
                PromptSelectionResult promptSelectionResult = editor.GetSelection(selectionFilter);
                if (promptSelectionResult.Status != PromptStatus.OK) return;
                SelectionSet selectionSet = promptSelectionResult.Value;
                foreach (var objectId in selectionSet.GetObjectIds())
                {
                    BlockReference blockReference = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;
                    if (blockReference == null || blockReference.Name != blockName1) continue;
                    blockReferences1.Add(blockReference);
                }
            }

            List<DiagramPanel> diagramPanels = new List<DiagramPanel>();
            foreach (var blockReference1 in blockReferences1)
            {
                diagramPanels.Add(GetDiagramPanel(blockReference1));
            }

            if (diagramPanels.Count == 0) return;
            List<DiagramPanel> ALPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][L][^TE]")).ToList();
            List<DiagramPanel> ALTPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][L][T]")).ToList();
            List<DiagramPanel> ALEPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][L][E]")).ToList();
            List<DiagramPanel> APPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][P][^TE]")).ToList();
            List<DiagramPanel> APTPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][P][T]")).ToList();
            List<DiagramPanel> APEPanels = diagramPanels.Where(p => Regex.IsMatch(p.Name, "[A][P][E]")).ToList();

            List<string> tempAreas = new List<string>();
            foreach (var item in diagramPanels)
            {
                tempAreas.Add(Regex.Match(item.Name, "^[0-9]").Value);
            }
            List<string> areas = tempAreas.Distinct().ToList();
            areas.Sort();

            List<string> tempFloors = new List<string>();
            foreach (var item in ALPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][L]\\w*\\d*").Value.Substring(2));
            }
            foreach (var item in ALTPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][L][T]\\w*\\d*").Value.Substring(3));
            }
            foreach (var item in ALEPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][L][E]\\w*\\d*").Value.Substring(3));
            }
            foreach (var item in APPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][P]\\w*\\d*").Value.Substring(2));
            }
            foreach (var item in APTPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][P][T]\\w*\\d*").Value.Substring(3));
            }
            foreach (var item in APEPanels)
            {
                tempFloors.Add(Regex.Match(item.Name, "[A][P][E]\\w*\\d*").Value.Substring(3));
            }

            List<string> floors = tempFloors.Distinct().ToList();
            floors.Sort();

            for (int i = 0; i < floors.Count - 1; i++)
            {
                for (int j = i + 1; j < floors.Count; j++)
                {
                    string temp = null;
                    string a = floors[i];
                    string b = floors[j];
                    if (a.Contains("B") && b.Contains("B"))
                    {
                        string a1 = Regex.Match(a, "[B]\\d*").Value.Substring(1);
                        string b1 = Regex.Match(b, "[B]\\d*").Value.Substring(1);

                        string a2 = Regex.Match(a, "[B][A-Za-z]").Value.Substring(1);
                        string b2 = Regex.Match(b, "[B][A-Za-z]").Value.Substring(1);

                        if (!string.IsNullOrEmpty(a1) && !string.IsNullOrEmpty(b1))
                        {
                            if (int.Parse(a1) < int.Parse(b1))
                            {
                                temp = a;
                                a = b;
                                b = temp;
                            }
                        }
                        else if (string.IsNullOrEmpty(a1) && !string.IsNullOrEmpty(b1))
                        {
                            temp = a;
                            a = b;
                            b = temp;
                        }
                        else if (!string.IsNullOrEmpty(a2) && string.IsNullOrEmpty(b2))
                        {
                            temp = a;
                            a = b;
                            b = temp;
                        }
                        else if (!string.IsNullOrEmpty(a2) && !string.IsNullOrEmpty(b1))
                        {
                            temp = a;
                            a = b;
                            b = temp;
                        }
                    }
                    else if (!a.Contains("B") && b.Contains("B"))
                    {
                        temp = a;
                        a = b;
                        b = temp;
                    }
                    else if (!a.Contains("B") && !b.Contains("B"))
                    {
                        string a1 = Regex.Match(a, "\\d*").Value;
                        string b1 = Regex.Match(b, "\\d*").Value;

                        string a2 = Regex.Match(a, "[A-Za-z]*").Value;
                        string b2 = Regex.Match(b, "[A-Za-z]*").Value;

                        if (!string.IsNullOrEmpty(a1) && !string.IsNullOrEmpty(b1))
                        {
                            if (int.Parse(a1) > int.Parse(b1))
                            {
                                temp = a;
                                a = b;
                                b = temp;
                            }
                        }
                        else if (string.IsNullOrEmpty(a1))
                        {
                            temp = a;
                            a = b;
                            b = temp;
                        }
                        else if (!string.IsNullOrEmpty(a2) && string.IsNullOrEmpty(b2))
                        {
                            temp = a;
                            a = b;
                            b = temp;
                        }
                    }
                }
            }

            string layerName = "E-ANNO-TEXT";
            int colorIndex = 7;
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            LayerTableRecord layerTableRecord = new LayerTableRecord();
            if (!layerTable.Has(layerName))
            {
                layerTableRecord.Name = layerName;
                layerTable.UpgradeOpen();
                layerTable.Add(layerTableRecord);
                transaction.AddNewlyCreatedDBObject(layerTableRecord, true);
                layerTable.DowngradeOpen();
            }

            layerTableRecord.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);

            ObjectId layerId = layerTable[layerName];
            if (database.Clayer != layerId)
            {
                database.Clayer = layerId;
            }

            string file1 = Path.Combine(directory, "多功能表2.dwg");
            if (!File.Exists(file1)) return;
            string file2 = Path.Combine(directory, "剩余电流动作报警器.dwg");
            if (!File.Exists(file2)) return;
            string file3 = Path.Combine(directory, "总电度表.dwg");
            if (!File.Exists(file3)) return;

            PromptStringOptions promptStringOptions = new PromptStringOptions("\n请输入竖向干线名称：")
            {
                DefaultValue = "竖向干线",
                AllowSpaces = true
            };
            PromptResult promptResult = editor.GetString(promptStringOptions);
            if (promptResult.Status != PromptStatus.OK) return;
            string verticalLineName = promptResult.StringResult;

            string[] labels = { "功率因数", "电度表", "电流互感器" };
            int labelIndex = 0;
            List<Point3d> list4 = new List<Point3d>();
            List<Point3d> list5 = new List<Point3d>();

            double offsetX = 3.0;
            double offsetY = 5.0;

            foreach (var floor in floors)
            {
                List<DiagramPanel> allPanels = diagramPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> AL = ALPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> ALT = ALTPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> ALE = ALEPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> AP = APPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> APT = APTPanels.Where(p => p.Name.Contains(floor)).ToList();
                List<DiagramPanel> APE = APEPanels.Where(p => p.Name.Contains(floor)).ToList();

                allPanels.AddRange(AL);
                allPanels.AddRange(ALT);
                allPanels.AddRange(ALE);
                allPanels.AddRange(AP);
                allPanels.AddRange(APT);
                allPanels.AddRange(APE);

                List<BlockReference> list6 = new List<BlockReference>();
                list6.AddRange(blockReferences1.Where(b => AL.Contains(GetDiagramPanel(b))).ToList());
                list6.AddRange(blockReferences1.Where(b => ALT.Contains(GetDiagramPanel(b))).ToList());
                list6.AddRange(blockReferences1.Where(b => ALE.Contains(GetDiagramPanel(b))).ToList());
                list6.AddRange(blockReferences1.Where(b => AP.Contains(GetDiagramPanel(b))).ToList());
                list6.AddRange(blockReferences1.Where(b => APT.Contains(GetDiagramPanel(b))).ToList());
                list6.AddRange(blockReferences1.Where(b => APE.Contains(GetDiagramPanel(b))).ToList());

                List<Point3d> list7 = new List<Point3d>();
                foreach (var blockReference2 in list6)
                {
                    list7.Add(blockReference2.Position.TransformBy(matrixd));
                }

                list7.Sort((x, y) => x.X.CompareTo(y.X));
                list4.Add(list7[0]);
                list5.Add(list7[0]);

                Point3d point = new Point3d(point3D4.X + offsetX, point3D4.Y - offsetY * labelIndex, 0);
                list4.Add(point);
                list5.Add(point);

                DBText text = new DBText
                {
                    Position = point,
                    Height = 2.5,
                    TextString = $"{verticalLineName}-{floor}",
                    Layer = layerName,
                    ColorIndex = colorIndex
                };
                modelSpace.UpgradeOpen();
                modelSpace.AppendEntity(text);
                transaction.AddNewlyCreatedDBObject(text, true);
                modelSpace.DowngradeOpen();

                list4.Add(new Point3d(point.X + 15, point.Y, 0));
                list5.Add(new Point3d(point.X + 15, point.Y, 0));

                for (int i = 0; i < labels.Length; i++)
                {
                    BlockReference blockReference3 = InsertBlock(file1, point3D4, labels[i], layerName, database, transaction);
                    list4.Add(blockReference3.Position);
                    list5.Add(blockReference3.Position);
                }

                list4.Add(point3D4);
                list5.Add(point3D4);
                labelIndex++;
            }

            Polyline polyline = new Polyline();
            for (int i = 0; i < list4.Count; i++)
            {
                polyline.AddVertexAt(i, new Point2d(list4[i].X, list4[i].Y), 0, 0, 0);
            }
            polyline.Layer = layerName;
            polyline.ColorIndex = colorIndex;
            modelSpace.UpgradeOpen();
            modelSpace.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);
            modelSpace.DowngradeOpen();

            Polyline polyline2 = new Polyline();
            for (int i = 0; i < list5.Count; i++)
            {
                polyline2.AddVertexAt(i, new Point2d(list5[i].X, list5[i].Y), 0, 0, 0);
            }
            polyline2.Layer = layerName;
            polyline2.ColorIndex = colorIndex;
            modelSpace.UpgradeOpen();
            modelSpace.AppendEntity(polyline2);
            transaction.AddNewlyCreatedDBObject(polyline2, true);
            modelSpace.DowngradeOpen();

            transaction.Commit();
        }

        private BlockReference InsertBlock(string filePath, Point3d point, string blockName, string layerName, Database database, Transaction transaction)
        {
            using Database blockDb = new Database(false, true);
            blockDb.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, "");

            ObjectId blockId = database.Insert(blockName, blockDb, false);
            BlockReference blockReference = new BlockReference(point, blockId);
            BlockTableRecord blockTableRecord = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
            blockReference.Layer = layerName;
            blockReference.ColorIndex = 7;

            blockTableRecord.AppendEntity(blockReference);
            transaction.AddNewlyCreatedDBObject(blockReference, true);

            return blockReference;
        }

        private DiagramPanel GetDiagramPanel(BlockReference blockReference)
        {
            string blockName = blockReference.Name;
            Match match = Regex.Match(blockName, @"(?<name>[A-Z]+)-(?<floor>[A-Z]+\d*)-(?<area>\d*)");
            return new DiagramPanel
            {
                Name = match.Groups["name"].Value,
                Floor = match.Groups["floor"].Value,
                Area = match.Groups["area"].Value
            };
        }
    }

    public class DiagramPanel
    {
        public string Name { get; set; }
        public string Floor { get; set; }
        public string Area { get; set; }
    }
    #endregion
}
