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

namespace SharpTrader.Plotting
{
    /// <summary>
    /// Logica di interazione per TraderBotResultsPlot.xaml
    /// </summary>
    public partial class TraderBotResultsPlot : Window
    {
        public TraderBotResultsPlot()
        {

            InitializeComponent();
            Plot1.IsManipulationEnabled = true;
        }

        private void PlotView_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {

        }
    }
}
