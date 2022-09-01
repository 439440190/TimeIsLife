﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TimeIsLifeInstallerClass
{
    internal class RegisterWindowViewModel: ObservableObject
    {
        public RegisterWindowViewModel(string filePath, RegisterWindow registerWindow)
        {
            AutoLoadDlls();
            RegisterWindow = registerWindow;
            FilePath = filePath;
            RegiserCommand = new RelayCommand(Regiser);
            SelectedCommand = new RelayCommand(Selected);
        }
        private void AutoLoadDlls()
        {

            List<string> cadKeyNames = new List<string>();
            // 获取HKEY_CURRENT_USER键
            RegistryKey keyCurrentUser = Registry.CurrentUser;
            // 打开AutoCAD所属的注册表键:HKEY_CURRENT_USER\Software\Autodesk\AutoCAD
            RegistryKey keyAutoCAD = keyCurrentUser.OpenSubKey("Software\\Autodesk\\AutoCAD");

            foreach (var keyCurAutoCAD in keyAutoCAD.GetSubKeyNames())
            {

                RegistryKey keyVersion = keyAutoCAD.OpenSubKey(keyCurAutoCAD);
                string language = keyVersion.GetValue("CurVer").ToString();
                RegistryKey keyLanguage = keyVersion.OpenSubKey(language);
                string cadKeyName = keyLanguage.Name.Substring(keyCurrentUser.Name.Length + 1);
                cadKeyNames.Add(cadKeyName);
            }

            foreach (var cadKeyName in cadKeyNames)
            {
                //打开HKEY_LOCAL_MACHINE下当前AutoCAD的注册表键以获得版本号
                RegistryKey keyCAD = Registry.LocalMachine.OpenSubKey(cadKeyName);
                //设置文本框显示当前AutoCAD版本号
                string cadName = keyCAD.GetValue("ProductName").ToString();
                Cads.Add(new CadKeyName(cadName, cadKeyName));
            }
        }

        public RegisterWindow RegisterWindow { get; set; }
        public string FilePath { get; set; }

        private CadKeyName selectedCadKeyName;
        public CadKeyName SelectedCadKeyName
        {
            get => selectedCadKeyName;
            set => SetProperty(ref selectedCadKeyName, value);
        }
        public List<CadKeyName> Cads { get; set; } = new List<CadKeyName>();

        List<CadKeyName> selectedCads = new List<CadKeyName>();

        public IRelayCommand RegiserCommand { get; }
        void Regiser()
        {
            RegisterWindow.Close();            
        }

        public IRelayCommand SelectedCommand { get; }
        void Selected()
        {
            if (SelectedCadKeyName.IsChecked == true)
            {
                if (!selectedCads.Contains(SelectedCadKeyName))
                {
                    AddRegistryKey(SelectedCadKeyName, FilePath);
                }
            }
            else
            {
                if (selectedCads.Contains(SelectedCadKeyName))
                {
                    RemoveRegistryKey(SelectedCadKeyName);
                }
            }

            void AddRegistryKey(CadKeyName selectedCadKeyName,string filePath)
            {
                string AppName = "TimeIsLife";
                string AppDesc = AppName;
                int FlagLOADCTRLS = 2;

                //打开HKEY_CURRENT_USER下当前AutoCAD的Applications注册表键以显示已加载的.NET程序
                RegistryKey keyApplications = Registry.CurrentUser.CreateSubKey(selectedCadKeyName.Key + "\\" + "Applications");
                //若存在同名的程序且选择不覆盖则返回
                if (keyApplications.GetSubKeyNames().Contains(AppName)) return;
                //创建相应的键并设置自动加载应用程序的选项
                RegistryKey keyUserApp = keyApplications.CreateSubKey(AppName);
                keyUserApp.SetValue("DESCRIPTION", AppDesc, RegistryValueKind.String);
                keyUserApp.SetValue("LOADCTRLS", FlagLOADCTRLS, RegistryValueKind.DWord);
                keyUserApp.SetValue("LOADER", filePath, RegistryValueKind.String);
                keyUserApp.SetValue("MANAGED", 1, RegistryValueKind.DWord);

                return;
            }

            void RemoveRegistryKey(CadKeyName selectedCadKeyName)
            {
                string AppName = "TimeIsLife";
                try
                {                    
                    // 以写的方式打开Applications注册表键
                    RegistryKey keyApp = Registry.CurrentUser.OpenSubKey(selectedCadKeyName.Key + "\\" + "Applications", true);
                    //删除指定名称的注册表键
                    keyApp.DeleteSubKeyTree(AppName);
                }
                catch
                {

                }
                return;
            }
        }
    }

    public class CadKeyName:ObservableObject
    {
        public CadKeyName(string name, string key)
        {
            Name = name;
            Key = key;
            IsChecked = false;
        }

        private string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        private string key;
        public string Key
        {
            get => key;
            set => SetProperty(ref key, value);
        }

        private bool isChecked;
        public bool IsChecked
        {
            get => isChecked;
            set => SetProperty(ref isChecked, value);
        }
    }
}
