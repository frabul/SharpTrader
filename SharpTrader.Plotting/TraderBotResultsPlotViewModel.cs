using MvvmFoundation.Wpf;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SharpTrader.Plotting
{

    /// <summary>
    /// N.B. This should be instantiated in the same threa as its window
    /// </summary>
    public class TraderBotResultsPlotViewModel : ObservableObject
    {
        private TraderBot Robot;
        private CandleStickSeries CandlesChart;
        private System.Timers.Timer timer;
        private Stopwatch yRangeUpdateWD = new Stopwatch();

        public int ChartWidth { get; set; } = 80;
        public PlotModel PlotViewModel { get; private set; }
        public bool Continue { get; set; }
        public string Symbol { get; set; }

        public Dispatcher Dispatcher { get; set; }

        private DateTimeAxis XAxis;
        private LinearAxis SymbolY;

        private List<(LineSeries line, Axis axis)> LinesOnDedicatedAxis = new List<(LineSeries, Axis)>();

        public TraderBotResultsPlotViewModel(TraderBot robot)
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            Robot = robot;

            this.PlotViewModel = new PlotModel { Title = robot.Drawer.Title };
            CandlesChart = new CandleStickSeries()
            {
                IsVisible = true,
                //CandleWidth = ChartCandleWidth * 0.5, 
            };
            CandlesChart.TrackerFormatString = "{0}\n{1}: {2}\nHigh: {3:0.#####}\nLow: {4:0.#####}\nOpen: {5:0.#####}\nClose: {6:0.#####}";
            XAxis = new DateTimeAxis()
            {

                Position = AxisPosition.Bottom,
                StringFormat = "yyyy/MM/dd HH:mm",
                Key = "Axis_X"
            };
            SymbolY = new LinearAxis() { Position = AxisPosition.Left };
            PlotViewModel.Axes.Add(XAxis);
            PlotViewModel.Axes.Add(SymbolY);
            PlotViewModel.KeyDown += PlotViewModel_KeyDown;
            timer = new System.Timers.Timer()
            {
                Interval = 100,
                AutoReset = true,

            };
            timer.Elapsed += UpdateOnTimerTick;
            timer.Start();
        }

        private void PlotViewModel_KeyDown(object sender, OxyKeyEventArgs e)
        {
            if (e.Key == OxyKey.R && e.ModifierKeys == OxyModifierKeys.None)
            {
                AdjustYAxisZoom();
                PlotViewModel.InvalidatePlot(false);
            }

        }

        private void UpdateOnTimerTick(object sender, EventArgs e)
        {
            if (yRangeUpdateWD.ElapsedMilliseconds > 300)
            {
                yRangeUpdateWD.Stop();
                yRangeUpdateWD.Reset();
                AdjustYAxisZoom();
            }
        }

        public void UpdateChart()
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.BeginInvoke(new Action(UpdateChart));
                return; 
            }

            //while (PlotViewModel.Axes.Count < 2)
            //    Thread.Sleep(100);

            PlotViewModel.Series.Clear();

            while (Robot.Drawer.Candles.Next())
            {
                this.CandlesChart.Items.Add(new ChartBar(Robot.Drawer.Candles.Tick));
            }
            if (this.CandlesChart.Items.Count < 1)
                return;

            PlotViewModel.Series.Add(CandlesChart);
            var lastTick = Robot.Drawer.Candles.Tick;


            //-----------------  HORIZONTAL LINES --------------
            int linesAdded = 0;
            var levels = new List<double>(Robot.Drawer.HorizontalLines);
            levels.Sort();
            for (int l = 1; l < levels.Count; l++)
                if (levels[l] - levels[l - 1] < 0.00001)
                    levels[l] = levels[l - 1] + 0.00001;

            foreach (var level in levels)
            {
                //if (level < range.High + range.Length && level > range.Low - range.Length)
                {
                    LineSeries line = new LineSeries()
                    {
                        MarkerStrokeThickness = 1,
                        LineStyle = LineStyle.Solid,
                        Color = OxyColor.FromArgb(200, 1, 1, 200),
                        StrokeThickness = 0.5f,
                    };

                    line.Points.Add(new DataPoint(0, level));
                    line.Points.Add(new DataPoint(PlotViewModel.Axes[0].Maximum - 1, level));
                    PlotViewModel.Series.Add(line);
                    linesAdded++;
                }
            }

            //----------------- lines ----------------------
            foreach (var line in Robot.Drawer.Lines)
            {
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.Color.A, line.Color.R, line.Color.G, line.Color.B),
                    StrokeThickness = 2f,
                };

                lineserie.Points.AddRange(
                    line.Points.Select(dot => new DataPoint(dot.X.ToAxisDouble(), dot.Y)));
                PlotViewModel.Series.Add(lineserie);
                linesAdded++;
            }

            foreach (var line in Robot.Drawer.LinesOnDedicatedAxis)
            {
                var axis = new LinearAxis() { Position = AxisPosition.Right, Key = "loda_1" };
                PlotViewModel.Axes.Add(axis);
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.Color.A, line.Color.R, line.Color.G, line.Color.B),
                    StrokeThickness = 3f,
                    YAxisKey = PlotViewModel.Axes[2].Key,
                    XAxisKey = XAxis.Key
                };


                lineserie.Points.AddRange(
                    line.Points.Select(dot => new DataPoint(dot.X.ToAxisDouble(), dot.Y)));
                PlotViewModel.Series.Add(lineserie);
                linesAdded++;

                LinesOnDedicatedAxis.Add((lineserie, axis));

            }

            //-------plot points ---------------
            var pointsSerie = new OxyPlot.Series.ScatterSeries() { MarkerSize = 15, MarkerType = MarkerType.Circle };
            for (int p = 0; p < Robot.Drawer.Points.Count; p++)
                pointsSerie.Points.Add(
                    new ScatterPoint(Robot.Drawer.Points[p].Time.ToAxisDouble(), Robot.Drawer.Points[p].Value, 5));
            PlotViewModel.Series.Add(pointsSerie);



            //---------- ADJUST X

            PlotViewModel.Axes[0].Minimum =
                Robot.Drawer.Candles.Tick.Time.Subtract(new TimeSpan(ChartWidth * lastTick.Timeframe.Ticks)).ToAxisDouble();
            PlotViewModel.Axes[0].Maximum = Robot.Drawer.Candles.Tick.Time.ToAxisDouble();
            PlotViewModel.Axes[0].Reset();
            //--------- ADJUST Y
            AdjustYAxisZoom();

            PlotViewModel.InvalidatePlot(true);
        }

        private void AdjustYAxisZoom()
        {
            var items = CandlesChart.Items;
            if (items.Count < 1)
                return;


            int i = items.Count;
            while (items[--i].X >= PlotViewModel.Axes[0].ActualMaximum)
                ;
            Candlestick range = new Candlestick()
            {
                High = items[i].High,
                Low = items[i].Low
            };
            while ((i > -1) && (items[i].X >= PlotViewModel.Axes[0].ActualMinimum))

            {
                if (range.High < items[i].High)
                    range.High = (float)items[i].High;
                if (range.Low > items[i].Low)
                    range.Low = (float)items[i].Low;
                i--;
            }

            PlotViewModel.Axes[1].Minimum = range.Low - (range.High - range.Low) * 0.03;
            PlotViewModel.Axes[1].Maximum = range.High + (range.High - range.Low) * 0.03;

            PlotViewModel.Axes[1].Reset();


            foreach (var lx in LinesOnDedicatedAxis)
            {
                double min = double.MaxValue;
                double max = double.MinValue;
                foreach (var p in lx.line.Points)
                {
                    if (p.X > XAxis.ActualMinimum)
                    {
                        min = p.Y < min ? p.Y : min;
                        max = p.Y > max ? p.Y : max;
                    }
                    if (p.X > XAxis.ActualMaximum)
                        break;
                }
                lx.axis.Minimum = min;
                lx.axis.Maximum = max;
                lx.axis.Reset();
            }

            //PlotViewModel.InvalidatePlot(false);
        }

        public static TraderBotResultsPlotViewModel RunWindow(TraderBot bot)
        {
            TraderBotResultsPlotViewModel vm = null;
            TraderBotResultsPlot Window = null;
            Thread newWindowThread = new Thread(new ThreadStart(() =>
            {
                vm = new Plotting.TraderBotResultsPlotViewModel(bot);
                // Create our context, and install it:
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(
                        Dispatcher.CurrentDispatcher));

                Window = new TraderBotResultsPlot();

                // When the window closes, shut down the dispatcher
                Window.Closed += (s, e) =>
                   Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                Window.DataContext = vm;
                Window.Show();
                // Start the Dispatcher Processing
                System.Windows.Threading.Dispatcher.Run();
            }));


            newWindowThread.SetApartmentState(ApartmentState.STA);
            // Make the thread a background thread
            newWindowThread.IsBackground = true;
            // Start the thread
            newWindowThread.Start();
            while (Window == null)
                Thread.Sleep(100);

            Thread.Sleep(600);
            return vm;
        }
    }

    static class Extensions
    {
        public static double ToAxisDouble(this DateTime dt)
        {
            return DateTimeAxis.ToDouble(dt);
        }
        public static double ToAxisDouble(this TimeSpan dt)
        {
            return DateTimeAxis.ToDouble(dt);
        }
    }

    public class ChartBar : HighLowItem
    {
        public DateTime Time { get; set; }
        public ChartBar(ICandlestick c)
            : base(c.Time.ToAxisDouble(), c.High, c.Low, c.Open, c.Close)
        {
            Time = c.Time;
        }
    }
}
