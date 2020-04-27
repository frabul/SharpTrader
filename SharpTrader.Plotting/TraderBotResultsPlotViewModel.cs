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
    public class PlottingHelper
    {
        public static void Show(PlotHelper plot)
        {
            var vm = TraderBotResultsPlotViewModel.RunWindow(plot);
            vm.UpdateChart();
        }
    }

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

        private DateTimeAxis XAxis => (DateTimeAxis)PlotViewModel.Axes[0];
        private LinearAxis Candles_Yaxis => (LinearAxis)PlotViewModel.Axes[1];

        private LinearAxis Volume_Yaxis => (LinearAxis)PlotViewModel.Axes[2];

        public bool IsAlive => !(this.Dispatcher.HasShutdownStarted || this.Dispatcher.HasShutdownFinished);

        public Axis[] AllAxes { get; private set; }
        public List<LineSeries> Lines { get; private set; } = new List<LineSeries>();

        public TraderBotResultsPlotViewModel(PlotHelper drawer)
        {
            Dispatcher = Dispatcher.CurrentDispatcher;
            Drawer = drawer;
            this.PlotViewModel = new PlotModel { Title = Drawer.Title };



            CreateAxes();
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


        }

        private void CreateAxes()
        {
            PlotViewModel.Axes.Clear();
            //--- X axis ---
            PlotViewModel.Axes.Add(new DateTimeAxis()
            {
                Position = AxisPosition.Bottom,
                StringFormat = "yyyy/MM/dd HH:mm",
                Key = "Axis_X"
            });

            //--- caldes y axis ---
            PlotViewModel.Axes.Add(new LinearAxis() { Key = "Candles", Position = AxisPosition.Left });

            //--- volume axis ---- 
            PlotViewModel.Axes.Add(new LinearAxis() { Key = "Volumes", Position = AxisPosition.Right, });

            //foreach line if there is no axis with given name add a new axis
            foreach (var line in Drawer.Lines.Where(l => l.AxisId != null))
            {
                if (!PlotViewModel.Axes.Any(a => a.Key == line.AxisId))
                    PlotViewModel.Axes.Add(new LinearAxis() { Key = line.AxisId, Position = AxisPosition.Right, });
            }

            //now we must resize all axes 
            //each additive axis takes a portion, the remaining part is for the candles
            var axesCount = PlotViewModel.Axes.Count - 2;
            double portion = 0.12;
            Candles_Yaxis.StartPosition = portion * axesCount;
            Candles_Yaxis.EndPosition = 1;
            for (int i = 2; i < PlotViewModel.Axes.Count; i++)
            {
                var start = portion * axesCount - (i - 1) * portion;
                PlotViewModel.Axes[i].StartPosition = start;
                PlotViewModel.Axes[i].EndPosition = start + portion;
            }
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

            //create exes
            CreateAxes();



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
                }
            }

            //----------------- lines ----------------------
            this.Lines.Clear();
            foreach (var line in Drawer.Lines)
            {
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 2,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.Color.A, line.Color.R, line.Color.G, line.Color.B),
                    StrokeThickness = 1.2f,
                    YAxisKey = line.AxisId ?? this.Candles_Yaxis.Key
                };

                lineserie.Points.AddRange(
                    line.Points.Select(dot => new DataPoint(dot.X.ToAxisDouble(), dot.Y)));
                PlotViewModel.Series.Add(lineserie);
                this.Lines.Add(lineserie);

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
                int index = Math.Min(288, Candles.Items.Count);
                var Xmin = Candles.Items[Candles.Items.Count - index].X;
                PlotViewModel.Axes[0].Minimum = Xmin;
                PlotViewModel.Axes[0].Maximum = Xmax;
                PlotViewModel.Axes[0].Reset();
            }

            //--------- ADJUST Y
            AdjustYAxisZoom();
            PlotViewModel.InvalidatePlot(true);
            return;
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

        private void AdjustYAxisZoom()
        {
            var xmin = XAxis.ActualMinimum;
            var xmax = XAxis.ActualMaximum;
            Dictionary<string, (double min, double max)> zooms = new Dictionary<string, (double min, double max)>();
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
                zooms[this.Candles_Yaxis.Key] = (ymin - margin, ymax + margin);
                this.Candles_Yaxis.Zoom(ymin - margin, ymax + margin);
                this.Volume_Yaxis.Zoom(0, volMax);
            }

            //adjust other lines
            //reset zoom of all exes 
            foreach (var line in this.Lines)
            {
                if (line.YAxisKey != this.Candles_Yaxis.Key)
                {
                    (double min, double max) zoom = (double.MaxValue, double.MinValue);
                    if (zooms.ContainsKey(line.YAxisKey))
                        zoom = zooms[line.YAxisKey];

                    foreach (var p in line.Points)
                    {
                        if (p.X > XAxis.ActualMinimum)
                        {
                            zoom.min = p.Y < zoom.min ? p.Y : zoom.min;
                            zoom.max = p.Y > zoom.max ? p.Y : zoom.max;
                        }
                        if (p.X > XAxis.ActualMaximum)
                            break;
                    }
                    zooms[line.YAxisKey] = zoom;
                }
            }
            foreach (var axis in PlotViewModel.Axes)
            {

                if (zooms.ContainsKey(axis.Key))
                {
                    var zoom = zooms[axis.Key];
                    axis.Zoom(zoom.min, zoom.max);
                }
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
                try
                {
                    Window.DataContext = vm;
                    Window.Show();
                    // Start the Dispatcher Processing
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (Exception ex)
                {
                }

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
        public ChartBar(ITradeBar c)
            : base(0, c.High, c.Low, c.Open, c.Close)
        {
            var time = c.CloseTime - TimeSpan.FromSeconds(c.Timeframe.TotalSeconds / 2);
            this.X = time.ToAxisDouble();
        }
    }
    public class ChartBarVol : OhlcvItem
    {
        
        public ChartBarVol(ITradeBar c)
            : base(c.Time.ToAxisDouble(), c.Open, c.High, c.Low, c.Close)
        {
            this.BuyVolume = c.Close > c.Open ? c.QuoteAssetVolume : 0;
            this.SellVolume = c.Close > c.Open ? 0 : c.QuoteAssetVolume; 
            this.X = c.OpenTime.ToAxisDouble();
        }
    }
}
