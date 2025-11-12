// (C) Copyright 2022 by  
//

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

using WinApp = System.Windows.Application;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using TimeIsLife.CADCommand;
using System.Windows.Input;
using Newtonsoft.Json;
using TimeIsLife.Model;
using TimeIsLife.ViewModel;
using System.Reflection;


// 该行不是必需的，但是可以提高加载性能
[assembly: ExtensionApplication(typeof(TimeIsLife.MyPlugin))]

namespace TimeIsLife
{
    // 该类由AutoCAD实例化一次，并在会话期间保持有效。 如果您一次都没有进行初始化，则应删除此类。
    public class MyPlugin : IExtensionApplication
    {
        void IExtensionApplication.Initialize()
        {
            // 在此处添加一次初始化一种常见的情况是在此处设置一个回调函数，供非托管代码调用。
            // 这样做:
            // 1. 从采用函数指针的非托管代码中导出函数，并将传入的值存储在全局变量中。
            // 2. 在此函数传递委托中调用此导出的函数。
            // 3. 当非托管代码需要此托管模块的服务时，您只需调用acrxLoadApp（）即可，直到acrxLoadApp返回全局函数指针时，它都会初始化为指向C＃委托。
            // 有关更多信息，请参见：
            // http://msdn2.microsoft.com/en-US/library/5zwkzwf4(VS.80).aspx
            // http://msdn2.microsoft.com/en-us/library/44ey4b32(VS.80).aspx
            // http://msdn2.microsoft.com/en-US/library/7esfatk4.aspx
            // 以及一些现有的AutoCAD托管应用程序。

            // 在此处初始化您的插件应用程序+
            Autodesk.Windows.ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
            // 设置全局兼容性属性
            //System.Windows.FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false;
            // 加载用户数据
            LoadUserData();

        }

        void IExtensionApplication.Terminate()
        {
            // 在这里清理插件应用程序
            // 保存用户数据
            SaveUserData();
        }

        #region 组件管理器初始化

        //组件管理器初始化
        public void ComponentManager_ItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                CreateRibbon();//添加ribbon菜单的函数
                ComponentManager.ItemInitialized -= ComponentManager_ItemInitialized;
            }
        }

        //根据XAML文件//添加ribbon菜单的函数
        public void CreateRibbon()
        {
            Uri uri = new Uri("/TimeIsLife;component/Resources/RibbonDictionary.xaml", UriKind.Relative);
            ResourceDictionary resourceDictionary = (ResourceDictionary)WinApp.LoadComponent(uri);
            RibbonTab tab = resourceDictionary["TimeIsLife"] as RibbonTab;

            //查找Ribbon按钮并添加命令事件
            RibbonItemCollection items = tab.Panels[0].Source.Items;
            foreach (RibbonItem item in items)
            {
                if (item is RibbonButton)
                    ((RibbonButton)item).CommandHandler = new RibbonCommandHandler();
                else if (item is RibbonRowPanel)
                {
                    RibbonRowPanel row = (RibbonRowPanel)item;
                    foreach (RibbonItem rowItem in row.Items)
                    {
                        if (rowItem is RibbonButton)
                            ((RibbonButton)rowItem).CommandHandler = new RibbonCommandHandler();
                    }
                }
            }
            RibbonControl ribbonControl = ComponentManager.Ribbon;//获取Ribbon界面
            ribbonControl.Tabs.Add(tab);//将选项卡添加到Ribbon界面中
            //ribbonControl.ActiveTab = tab;//设置当前活动选项卡
        }

        #endregion

        #region 载入个人数据
        private readonly string userDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeIsLife", "CurrentUserData.json");
        public static UserData CurrentUserData { get; set; }
        private void LoadUserData()
        {
            // 假设我们的用户数据存储在一个JSON文件中
            // 读取本地文件中的JSON字符串
            if (!File.Exists(userDataPath))
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                string blockDirectory = Path.Combine(assemblyDirectory, "Block", "FA");
                string schematicBlockDirectory = Path.Combine(assemblyDirectory, "SchematicBlock", "FA");

                CurrentUserData = new UserData();
                CurrentUserData.FloorLayerName = "Timeislife-Floor";
                CurrentUserData.FireAreaLayerName = "Timeislife-FireArea";
                CurrentUserData.RoomLayerName = "Timeislife-Room";
                CurrentUserData.AvoidanceAreaLayerName = "Timeislife-AvoidanceArea";
                CurrentUserData.EquipmentLayerName = "E-EQUIP";
                CurrentUserData.WireLayerName = "E-WIRE";
                CurrentUserData.SlabThickness = 120;
                CurrentUserData.TreeOrCircle = "Tree";
                CurrentUserData.FireAlarmEquipments = GetFireAlarmEquipments(blockDirectory, schematicBlockDirectory);
                CurrentUserData.DeviceType = "JXX";
                CurrentUserData.IsUseAvoidanceArea = false;
                CurrentUserData.AlignXY = "X";
                CurrentUserData.EntityOrNestedEntity = "E";
                return;
            }

            string jsonData = File.ReadAllText(userDataPath);
            CurrentUserData = JsonConvert.DeserializeObject<UserData>(jsonData);
        }

        private void SaveUserData()
        {
            string jsonData = JsonConvert.SerializeObject(CurrentUserData, Formatting.Indented);
            File.WriteAllText(userDataPath, jsonData);
        }

        private ObservableCollection<FireAlarmEquipment> GetFireAlarmEquipments(string blockDirectory, string schematicBlockDirectory)
        {
            return new ObservableCollection<FireAlarmEquipment>()
            {
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa01, "接线端子箱",Path.Combine(blockDirectory,"FA-01-接线端子箱.dwg"),Path.Combine(schematicBlockDirectory,MyPlugin.CurrentUserData.TreeOrCircle,"FA-01-接线端子箱.dwg"),true,true,2500),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa02, "带消防电话插孔的手动报警按钮",Path.Combine(blockDirectory,"FA-02-带消防电话插孔的手动报警按钮.dwg"),Path.Combine(schematicBlockDirectory,"FA-02-带消防电话插孔的手动报警按钮.dwg"),true,true,700),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa03, "火灾报警电话机",Path.Combine(blockDirectory,"FA-03-火灾报警电话机.dwg"),Path.Combine(schematicBlockDirectory,"FA-03-火灾报警电话机.dwg"),true,true,700),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa04, "声光警报器", Path.Combine(blockDirectory, "FA-04-声光警报器.dwg"), Path.Combine(schematicBlockDirectory, "FA-04-声光警报器.dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa05, "3W火灾警报扬声器(挂墙明装,距地2.4m)", Path.Combine(blockDirectory, "FA-05-3W火灾警报扬声器(挂墙明装距地2.4m).dwg"), Path.Combine(schematicBlockDirectory, "FA-05-3W火灾警报扬声器(挂墙明装距地2.4m).dwg"), true, false, 1600),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa06, "3W火灾警报扬声器(吸顶安装)", Path.Combine(blockDirectory, "FA-06-3W火灾警报扬声器(吸顶安装).dwg"), Path.Combine(schematicBlockDirectory, "FA-06-3W火灾警报扬声器(吸顶安装).dwg"), true, true, 700),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa07, "消火栓起泵按钮", Path.Combine(blockDirectory, "FA-07-消火栓起泵按钮.dwg"), Path.Combine(schematicBlockDirectory, "FA-07-消火栓起泵按钮.dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa08, "智能型点型感烟探测器", Path.Combine(blockDirectory, "FA-08-智能型点型感烟探测器.dwg"), Path.Combine(schematicBlockDirectory, "FA-08-智能型点型感烟探测器.dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa09, "智能型点型感温探测器", Path.Combine(blockDirectory, "FA-09-智能型点型感温探测器.dwg"), Path.Combine(schematicBlockDirectory, "FA-09-智能型点型感温探测器.dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa10, "防火阀(70°C熔断关闭)", Path.Combine(blockDirectory, "FA-10-防火阀(70°C熔断关闭).dwg"), Path.Combine(schematicBlockDirectory, "FA-10-防火阀(70°C熔断关闭).dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa11, "防火阀(280°C熔断关闭)", Path.Combine(blockDirectory, "FA-11-防火阀(280°C熔断关闭).dwg"), Path.Combine(schematicBlockDirectory, "FA-11-防火阀(280°C熔断关闭).dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa12, "电动排烟阀(常闭)", Path.Combine(blockDirectory, "FA-12-电动排烟阀(常闭).dwg"), Path.Combine(schematicBlockDirectory, "FA-12-电动排烟阀(常闭).dwg"), true, true, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa13, "电动排烟阀(常开)", Path.Combine(blockDirectory, "FA-13-电动排烟阀(常开).dwg"), Path.Combine(schematicBlockDirectory, "FA-13-电动排烟阀(常开).dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa14, "常闭正压送风口", Path.Combine(blockDirectory, "FA-14-常闭正压送风口.dwg"), Path.Combine(schematicBlockDirectory, "FA-14-常闭正压送风口.dwg"), true, false, 900),
                // 继续之前的实例化
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa15, "防火卷帘控制器", Path.Combine(blockDirectory, "FA-15-防火卷帘控制器.dwg"), Path.Combine(schematicBlockDirectory, "FA-15-防火卷帘控制器.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa16, "电动挡烟垂壁控制箱", Path.Combine(blockDirectory, "FA-16-电动挡烟垂壁控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-16-电动挡烟垂壁控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa17, "水流指示器", Path.Combine(blockDirectory, "FA-17-水流指示器.dwg"), Path.Combine(schematicBlockDirectory, "FA-17-水流指示器.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa18, "信号阀", Path.Combine(blockDirectory, "FA-18-信号阀.dwg"), Path.Combine(schematicBlockDirectory, "FA-18-信号阀.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa19, "智能线型红外光束感烟探测器（发射端）", Path.Combine(blockDirectory, "FA-19-智能线型红外光束感烟探测器（发射端）.dwg"), Path.Combine(schematicBlockDirectory, "FA-19-智能线型红外光束感烟探测器（发射端）.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa20, "智能线型红外光束感烟探测器（接收端）", Path.Combine(blockDirectory, "FA-20-智能线型红外光束感烟探测器（接收端）.dwg"), Path.Combine(schematicBlockDirectory, "FA-20-智能线型红外光束感烟探测器（接收端）.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa21, "电动排烟窗控制箱", Path.Combine(blockDirectory, "FA-21-电动排烟窗控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-21-电动排烟窗控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa22, "消防电梯控制箱", Path.Combine(blockDirectory, "FA-22-消防电梯控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-22-消防电梯控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa23, "电梯控制箱", Path.Combine(blockDirectory, "FA-23-电梯控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-23-电梯控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa24, "湿式报警阀组", Path.Combine(blockDirectory, "FA-24-湿式报警阀组.dwg"), Path.Combine(schematicBlockDirectory, "FA-24-湿式报警阀组.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa25, "预作用报警阀组", Path.Combine(blockDirectory, "FA-25-预作用报警阀组.dwg"), Path.Combine(schematicBlockDirectory, "FA-25-预作用报警阀组.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa26, "可燃气体探测控制器", Path.Combine(blockDirectory, "FA-26-可燃气体探测控制器.dwg"), Path.Combine(schematicBlockDirectory, "FA-26-可燃气体探测控制器.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa27, "流量开关", Path.Combine(blockDirectory, "FA-27-流量开关.dwg"), Path.Combine(schematicBlockDirectory, "FA-27-流量开关.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa28, "非消防配电箱", Path.Combine(blockDirectory, "FA-28-非消防配电箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-28-非消防配电箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa29, "消防泵控制箱", Path.Combine(blockDirectory, "FA-29-消防泵控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-29-消防泵控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa30, "喷淋泵控制箱", Path.Combine(blockDirectory, "FA-30-喷淋泵控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-30-喷淋泵控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa31, "消防稳压泵控制箱", Path.Combine(blockDirectory, "FA-31-消防稳压泵控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-31-消防稳压泵控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa32, "雨淋泵控制箱", Path.Combine(blockDirectory, "FA-32-雨淋泵控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-32-雨淋泵控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa33, "水幕泵控制箱", Path.Combine(blockDirectory, "FA-33-水幕泵控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-33-水幕泵控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa34, "消防风机控制箱", Path.Combine(blockDirectory, "FA-34-消防风机控制箱.dwg"), Path.Combine(schematicBlockDirectory, "FA-34-消防风机控制箱.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa35, "就地液位显示盘", Path.Combine(blockDirectory, "FA-35-就地液位显示盘.dwg"), Path.Combine(schematicBlockDirectory, "FA-35-就地液位显示盘.dwg"), true, false, 4000),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa37, "区域显示器", Path.Combine(blockDirectory, "FA-37-区域显示器.dwg"), Path.Combine(schematicBlockDirectory, "FA-37-区域显示器.dwg"), true, false, 1300),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa38, "常闭防火门监控模块", Path.Combine(blockDirectory, "FA-38-常闭防火门监控模块.dwg"), Path.Combine(schematicBlockDirectory, "FA-38-常闭防火门监控模块.dwg"), true, false, 1300),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa39, "常开防火门监控模块", Path.Combine(blockDirectory, "FA-39-常开防火门监控模块.dwg"), Path.Combine(schematicBlockDirectory, "FA-39-常开防火门监控模块.dwg"), true, false, 1300),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa40, "压力开关", Path.Combine(blockDirectory, "FA-40-压力开关.dwg"), Path.Combine(schematicBlockDirectory, "FA-40-压力开关.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa41, "火焰探测器", Path.Combine(blockDirectory, "FA-41-火焰探测器.dwg"), Path.Combine(schematicBlockDirectory, "FA-41-火焰探测器.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa42, "电磁阀", Path.Combine(blockDirectory, "FA-42-电磁阀.dwg"), Path.Combine(schematicBlockDirectory, "FA-42-电磁阀.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa43, "门禁控制器", Path.Combine(blockDirectory, "FA-43-门禁控制器.dwg"), Path.Combine(schematicBlockDirectory, "FA-43-门禁控制器.dwg"), true, false, 900),
                new FireAlarmEquipment(FireAlarmEquipmentType.Fa69, "总线短路隔离器", Path.Combine(blockDirectory, "FA-总线短路隔离器.dwg"), string.Empty, false, false, 900),
            };
        }

        #endregion
    }
}
