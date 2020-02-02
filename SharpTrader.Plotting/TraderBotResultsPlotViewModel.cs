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
        private PlotHelper Drawer;
        private CandleStickSeries Candles;
        private VolumeSeries Volumes;
        private System.Timers.Timer timer;
        private Stopwatch yRangeUpdateWD = new Stopwatch();

        public int ChartWidth { get; set; } = 80;
        public PlotModel PlotViewModel { get; private set; }
        public bool Continue { get; set; }
        public string Symbol { get; set; }

        public Dispatcher Dispatcher { get; set; }

        private DateTimeAxis XAxis;
        private LinearAxis Candles_Yaxis;

        private List<(LineSeries line, Axis axis)> LinesOnDedicatedAxis = new List<(LineSeries, Axis)>();
        private LinearAxis Volume_Yaxis;

        public bool IsAlive => !(this.Dispatcher.HasShutdownStarted || this.Dispatcher.HasShutdownFinished);

        public Axis[] DefaultAxes { get; private set; }

        public TraderBotResultsPlotViewModel(PlotHelper drawer)
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            Drawer = drawer;
            this.PlotViewModel = new PlotModel { Title = Drawer.Title };

            //add x axis 
            XAxis = new DateTimeAxis()
            {
                Position = AxisPosition.Bottom,
                StringFormat = "yyyy/MM/dd HH:mm",
                Key = "Axis_X"
            };
            PlotViewModel.Axes.Add(XAxis);

            Candles_Yaxis = new LinearAxis() { Key = "Candles", Position = AxisPosition.Left };
            PlotViewModel.Axes.Add(Candles_Yaxis);

            Volume_Yaxis = new LinearAxis()
            {
                Key = "Volumes",
                Position = AxisPosition.Right,
                StartPosition = 0.0,
                EndPosition = 0.25,

            };
            PlotViewModel.Axes.Add(Volume_Yaxis);

            Candles = new CandleStickSeries();
            Candles.TrackerFormatString = "{0}\n{1}: {2}\nHigh: {3:0.#####}\nLow: {4:0.#####}\nOpen: {5:0.#####}\nClose: {6:0.#####}";

            Volumes = new VolumeSeries()
            {
                PositiveHollow = false,
                VolumeStyle = VolumeStyle.Stacked,
                YAxisKey = Volume_Yaxis.Key,
            };

            PlotViewModel.KeyDown += PlotViewModel_KeyDown;
            timer = new System.Timers.Timer()
            {
                Interval = 100,
                AutoReset = true,
            };
            timer.Elapsed += UpdateOnTimerTick;
            timer.Start();

            DefaultAxes = PlotViewModel.Axes.ToArray();
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
        public void ShowWholeChart()
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.BeginInvoke(new Action(ShowWholeChart));
                return;
            }

            PlotViewModel.Axes[0].Minimum = PlotViewModel.Axes[0].DataMinimum;
            PlotViewModel.Axes[0].Maximum = PlotViewModel.Axes[0].Maximum;
            PlotViewModel.Axes[0].Reset();
            AdjustYAxisZoom();
        }

        public void UpdateChart()
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.BeginInvoke(new Action(UpdateChart));
                return;
            }

            //--- remove all series
            PlotViewModel.Series.Clear();
            while (Drawer.Candles.MoveNext())
            {
                this.Candles.Items.Add(new ChartBar(Drawer.Candles.Current));
                this.Volumes.Items.Add(new ChartBarVol(Drawer.Candles.Current) { });
            }
            //remove all axes and readd base axes
            PlotViewModel.Axes.Clear();
            foreach (var axis in DefaultAxes)
                PlotViewModel.Axes.Add(axis);
            //---
            ITradeBar lastTick = null;
            if (Candles.Items.Count > 0)
            {
                PlotViewModel.Series.Add(Candles);
                PlotViewModel.Series.Add(Volumes);
                //draw volumes
                //PlotViewModel.Series.Add(Volumes);
                lastTick = Drawer.Candles.Current;
            }

            //-----------------  HORIZONTAL LINES --------------
            int linesAdded = 0;
            var levels = new List<double>(Drawer.HorizontalLines);
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
            foreach (var line in Drawer.Lines)
            {
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 2,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.Color.A, line.Color.R, line.Color.G, line.Color.B),
                    StrokeThickness = 2f,
                    YAxisKey = this.Candles_Yaxis.Key
                };

                lineserie.Points.AddRange(
                    line.Points.Select(dot => new DataPoint(dot.X.ToAxisDouble(), dot.Y)));
                PlotViewModel.Series.Add(lineserie);
                linesAdded++;

                if (lastTick == null)
                    lastTick = new Candlestick()
                    {
                        OpenTime = line.Points[line.Points.Count - 2].X,
                        CloseTime = line.Points[line.Points.Count - 1].X,
                    };
            }

            int cnt = 0;
            foreach (var line in Drawer.LinesOnDedicatedAxis)
            {
                cnt++;
                var axis = new LinearAxis() { Position = AxisPosition.Right, Key = "loda_" + cnt };
                PlotViewModel.Axes.Add(axis);
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.Color.A, line.Color.R, line.Color.G, line.Color.B),
                    StrokeThickness = 1f,
                    YAxisKey = axis.Key,
                    XAxisKey = XAxis.Key
                };

                lock (line)
                    lineserie.Points.AddRange(
                        line.Points.Select(dot => new DataPoint(dot.X.ToAxisDouble(), dot.Y)));

                PlotViewModel.Series.Add(lineserie);
                linesAdded++;

                LinesOnDedicatedAxis.Add((lineserie, axis));

                if (lastTick == null)
                    lastTick = new Candlestick()
                    {
                        OpenTime = line.Points[line.Points.Count - 2].X,
                        CloseTime = line.Points[line.Points.Count - 1].X,
                    };
            }

            //-------plot points ---------------
            var pointsSerie = new OxyPlot.Series.ScatterSeries() { MarkerSize = 15, MarkerType = MarkerType.Circle };
            for (int p = 0; p < Drawer.Points.Count; p++)
                pointsSerie.Points.Add(
                    new ScatterPoint(Drawer.Points[p].Time.ToAxisDouble(), Drawer.Points[p].Value, 5));
            PlotViewModel.Series.Add(pointsSerie);

            //---------- ADJUST X to show 100 candles
            if (lastTick != null)
            {
                var Xmax = Candles.Items[Candles.Items.Count - 1].X;
                var Xmin = Candles.Items[Candles.Items.Count - 288].X;
                PlotViewModel.Axes[0].Minimum = Xmin;
                PlotViewModel.Axes[0].Maximum = Xmax;
                PlotViewModel.Axes[0].Reset();
            }

            //--------- ADJUST Y
            AdjustYAxisZoom();
            PlotViewModel.InvalidatePlot(true);
            return;
        }

        private void AdjustYAxisZoom()
        {
            var xmin = XAxis.ActualMinimum;
            var xmax = XAxis.ActualMaximum;

            //adcjust candles and volume
            if (Candles.Items.Count > 0)
            {
                var istart = Candles.FindByX(xmin);
                var iend = Candles.FindByX(xmax, istart);

                var ymin = double.MaxValue;
                var ymax = double.MinValue;

                var volMin = double.MaxValue;
                var volMax = double.MinValue;
                for (int i = istart; i <= iend; i++)
                {
                    var bar = Candles.Items[i];
                    ymin = Math.Min(ymin, bar.Low);
                    ymax = Math.Max(ymax, bar.High);

                    var vol = Volumes.Items[i].BuyVolume + Volumes.Items[i].SellVolume;
                    volMin = Math.Min(volMin, vol);
                    volMax = Math.Max(volMax, vol);
                }

                var extent = ymax - ymin;
                var margin = extent * 0.10;

                this.Candles_Yaxis.Zoom(ymin - margin, ymax + margin);
                this.Volume_Yaxis.Zoom(0, volMax);
            }

            //adjust other lines
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
                lx.axis.Zoom(min, max);
            }

        }

        public static TraderBotResultsPlotViewModel RunWindow(PlotHelper plot)
        {
            TraderBotResultsPlotViewModel vm = null;
            TraderBotResultsPlot Window = null;
            Thread newWindowThread = new Thread(new ThreadStart(() =>
            {
                vm = new Plotting.TraderBotResultsPlotViewModel(plot);
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
        public ChartBar(ITradeBar c)
            : base(c.Time.ToAxisDouble(), c.High, c.Low, c.Open, c.Close)
        {
            Time = c.Time;
        }
    }
    public class ChartBarVol : OhlcvItem
    {
        public DateTime Time { get; set; }
        public ChartBarVol(ITradeBar c)
            : base(c.Time.ToAxisDouble(), c.Open, c.High, c.Low, c.Close)
        {
            this.BuyVolume = c.Close > c.Open ? c.Volume : 0;
            this.SellVolume = c.Close > c.Open ? 0 : c.Volume;
            Time = c.Time;
        }
    }
}
