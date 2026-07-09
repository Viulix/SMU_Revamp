using Avalonia.Controls;
using System.Collections.Generic;
using SMU_Revamp.Models;

namespace SMU_Revamp.Views
{
    public partial class EnlargedResultPlotWindow : Window
    {
        public EnlargedResultPlotWindow()
        {
            InitializeComponent();
        }

        public EnlargedResultPlotWindow(string title, List<CurvePoint> points)
        {
            InitializeComponent();
            PlotTitleText.Text = title;
            
            // Set up the linear plot
            LinearPlot.Title = title;
            LinearPlot.Points = points;
            
            // Set up the log plot
            LogPlot.Title = title + " (Logarithmic)";
            LogPlot.Points = points;
        }
    }
}
