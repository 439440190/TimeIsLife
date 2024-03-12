﻿using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using DotNetARX;

using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NetTopologySuite;

using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using TimeIsLife.Jig;
using TimeIsLife.Model;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Point = NetTopologySuite.Geometries.Point;
using TimeIsLife.Helper;


namespace TimeIsLife.CADCommand
{
    internal partial class TilCommand
    {
        /// <summary>
        /// 获取NTS指定精度和标准坐标系的GeometryFactory实例
        /// </summary>
        /// <returns>GeometryFactory实例</returns>
        private GeometryFactory CreateGeometryFactory()
        {
            //NTS
            var precisionModel = new PrecisionModel(1000d);
            GeometryPrecisionReducer precisionReducer = new GeometryPrecisionReducer(precisionModel);
            NetTopologySuite.NtsGeometryServices.Instance = new NetTopologySuite.NtsGeometryServices
                (
                NetTopologySuite.Geometries.Implementation.CoordinateArraySequenceFactory.Instance,
                precisionModel,
                4326
                );
            return NtsGeometryServices.Instance.CreateGeometryFactory(precisionModel);
        }



        #region FF_ExplodeMInsertBlock
        [CommandMethod("FF_ExplodeMInsertBlock")]
        public void FF_ExplodeMInsertBlock()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            try
            {

                PromptSelectionOptions promptSelectionOptions = new PromptSelectionOptions()
                {
                    SingleOnly = true,
                    RejectObjectsOnLockedLayers = true,
                };

                TypedValueList typedValues = new TypedValueList();
                typedValues.Add(typeof(BlockReference));
                SelectionFilter selectionFilter = new SelectionFilter(typedValues);
                PromptSelectionResult promptSelectionResult = editor.GetSelection(promptSelectionOptions, selectionFilter);

                if (promptSelectionResult.Status == PromptStatus.OK)
                {
                    SelectionSet selectionSet = promptSelectionResult.Value;
                    foreach (var id in selectionSet.GetObjectIds())
                    {
                        MInsertBlock mInsertBlock = transaction.GetObject(id, OpenMode.ForWrite) as MInsertBlock;
                        if (mInsertBlock == null) continue;
                        mInsertBlock.ExplodeToOwnerSpace();
                        mInsertBlock.Erase();
                    }
                }

                transaction.Commit();
            }
            catch
            {
            }

        }
        #endregion

        #region FF_ModifyTextStyle

        /// 文字样式校正
        /// </summary>
        [CommandMethod("FF_ModifyTextStyle")]
        public void FF_ModifyTextStyle()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            Matrix3d ucsToWcsMatrix3d = editor.CurrentUserCoordinateSystem;

            string sysFontsPath = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);//windows系统字体目录
            DirectoryInfo sysDirInfo = new DirectoryInfo(sysFontsPath);//Windows系统字体文件夹

            using (Transaction transaction = document.TransactionManager.StartOpenCloseTransaction())
            {
                TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead, false);
                foreach (ObjectId id in textStyleTable)
                {
                    using TextStyleTableRecord textStyleTableRecord = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForWrite, false);
                    #region 校正windows系统字体
                    if (textStyleTableRecord.Font.TypeFace != string.Empty)
                    {
                        string fontFileFullName = string.Empty;

                        FileInfo[] fis = sysDirInfo.GetFiles(textStyleTableRecord.FileName);
                        if (fis.Length > 0)
                        {
                            fontFileFullName = fis[0].FullName;
                        }
                        else
                        {
                            fontFileFullName = FindFontFile(database, textStyleTableRecord.FileName);
                        }

                        if (fontFileFullName != string.Empty)
                        {
                            using (PrivateFontCollection privateFontCollection = new PrivateFontCollection())
                            {
                                try
                                {
                                    privateFontCollection.AddFontFile(fontFileFullName);

                                    //更正文字样式的字体名
                                    if (privateFontCollection.Families[0].Name != textStyleTableRecord.Font.TypeFace)
                                    {
                                        textStyleTableRecord.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(
                                            privateFontCollection.Families[0].Name, textStyleTableRecord.Font.Bold, textStyleTableRecord.Font.Italic,
                                            textStyleTableRecord.Font.CharacterSet, textStyleTableRecord.Font.PitchAndFamily
                                            );
                                    }
                                }
                                catch (System.Exception e)
                                {
                                    editor.WriteMessage($"\n***错误***：{fontFileFullName}-{e.Message}");
                                }
                            }
                        }
                        else
                        {
                            //字体缺失,则用宋体代替
                            textStyleTableRecord.FileName = "SimSun.ttf";
                            textStyleTableRecord.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("宋体", false, false, 134, 2);
                        }
                    }
                    #endregion
                    #region 校正shx字体
                    else
                    {
                        if (!textStyleTableRecord.IsShapeFile &&
                            FindFontFile(database, textStyleTableRecord.FileName) == string.Empty)
                        {
                            textStyleTableRecord.FileName = "romans.shx";//用romans.shx代替
                        }

                        if (textStyleTableRecord.BigFontFileName != string.Empty &&
                            FindFontFile(database, textStyleTableRecord.BigFontFileName) == string.Empty)
                        {
                            textStyleTableRecord.BigFontFileName = "hztxt.shx";//用gbcbig.shx代替
                        }
                    }
                    #endregion
                }

                transaction.Commit();
            }

            editor.Regen();
            editor.UpdateScreen();
        }

        private static string FindFontFile(Database db, string name)
        {
            var hostapp = HostApplicationServices.Current;

            if (name == "") return string.Empty;

            string fullname = string.Empty;
            try
            {
                fullname = hostapp.FindFile(name, db, FindFileHint.FontFile);
            }
            catch { }

            return fullname;
        }
        #endregion
    }
}
