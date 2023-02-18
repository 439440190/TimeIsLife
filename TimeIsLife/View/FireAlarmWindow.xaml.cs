﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TimeIsLife.ViewModel;

namespace TimeIsLife.View
{
    /// <summary>
    /// FireAlarmWindow.xaml 的交互逻辑
    /// </summary>
    public partial class FireAlarmWindow : Window
    {
        public static FireAlarmWindow instance;
        public FireAlarmWindow()
        {
            InitializeComponent();
            DataContext = FireAlarmWindowViewModel.instance;
            instance = this;
        }
    }
}
