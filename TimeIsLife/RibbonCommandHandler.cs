// (C) Copyright 2022 by  
//

using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace TimeIsLife
{
    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public bool CanExecute(object parameter)
        {
            return true;//确定此命令可以在其当前状态下执行
        }
        //当出现影响是否应执行该命令的更改时发生
        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter)
        {
            //获取发出命令的按钮对象
            RibbonButton button = parameter as RibbonButton;
            //如果发出命令的不是按钮或按钮未定义命令参数，则返回
            if (button == null || button.CommandParameter == null) return;
            //根据按钮的命令参数，执行对应的AutoCAD命令
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc.SendStringToExecute($"{button.CommandParameter.ToString()}\n", true, false, true);
        }
    }

}