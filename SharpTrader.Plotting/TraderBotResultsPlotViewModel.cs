using MvvmFoundation.Wpf;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
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


    public class TraderBotResultsPlotViewModel : ObservableObject
    {
        private string _BiasSign;
        private string _CurrentPositionString;
        public int ChartCandleWidth;
        public int ChartCandleStep;
        public int ChartWidth { get; set; }
        bool StopOnBar = true;
        bool StopOnPosition = true;

        TraderBot Robot;
        IMarketApi market;
        CandleStickSeries CandlesChart;


        public TraderBotResultsPlotViewModel(TraderBot roboto)
        {
            Robot = roboto;
            StopOnBar = true;
            StopOnPosition = true;

            this.PlotViewModel = new PlotModel { Title = "Tradings" };
            ChartWidth = 100;
            ChartCandleWidth = 2;
            ChartCandleStep = 225;
            CandlesChart = new CandleStickSeries() { IsVisible = true, /*CandleWidth = ChartCandleWidth * 0.5,*/ };
            CandlesChart.TrackerFormatString = "{0}\n{1}: {2}\nHigh: {3:0.#####}\nLow: {4:0.#####}\nOpen: {5:0.#####}\nClose: {6:0.#####}";


            PlotViewModel.Axes.Add(new DateTimeAxis() { Position = AxisPosition.Bottom });
            PlotViewModel.Axes.Add(new LinearAxis() { Position = AxisPosition.Left, MajorStep = 0.0050, MinorStep = 0.0010 });

            PlotViewModel.InvalidatePlot(true);
        }

        public PlotModel PlotViewModel { get; private set; }
        public bool Continue { get; set; }

        public string BiasSign
        {
            get { return _BiasSign; }
            set { _BiasSign = value; RaisePropertyChanged("BiasSign"); }
        }

        public string CurrentPositionString
        {
            get { return _CurrentPositionString; }
            set { _CurrentPositionString = value; RaisePropertyChanged("CurrentPositionString"); }
        }

        public void RunWindow()
        {
            Thread newWindowThread = new Thread(new ThreadStart(() =>
            {
                // Create our context, and install it:
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(
                        Dispatcher.CurrentDispatcher));

                Window = new TraderBotResultsPlot
                {
                    DataContext = this
                };
                Window.Show();
                // When the window closes, shut down the dispatcher
                Window.Closed += (s, e) =>
                   Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);

                Window.Show();
                // Start the Dispatcher Processing
                System.Windows.Threading.Dispatcher.Run();
            }));
            newWindowThread.SetApartmentState(ApartmentState.STA);
            // Make the thread a background thread
            newWindowThread.IsBackground = true;
            // Start the thread
            newWindowThread.Start();
            //TimeSerie<ICandlestick> ts;
            //using (var file = File.Open("D:\\progettiBck\\Forex\\Data\\EurUsdData.protobuf", FileMode.Open))
            //{

            //    var res = Serializer.Deserialize<List<Candle>>(file);
            //    ts = new SharpForex4.TimeSerie<SharpForex4.ICandlestick>(60, res.Count);
            //    foreach (var cand in res)
            //    {
            //        ts.AddRecord(cand.Date, cand);
            //    }

            //}
            //Robot = new SharpForex4.Strategies.Strategy_PriceActionAtSupportLevel();
            //var act = new ThreadStart(() =>
            //{
            //    Simulator = new SharpForex4.Simulator.MarketSimulator(ts, 10000);
            //    Simulator.NewBar += simulator_NewBar;
            //    Simulator.NewPosition += simulator_NewPosition;
            //    Simulator.PositionClosed += Simulator_PositionClosed;
            //    Simulator.Simulate(Robot, new DateTime(2014, 01, 1));
            //});
            //Thread t = new Thread(act);
            //t.IsBackground = true;
            //t.Start();
        }

        ChartBar CurBar = null;
        int StepCounter = 0;
        private TraderBotResultsPlot Window;

        private void Simulator_NewBar(ICandlestick c)
        {
            //CurBar = new ChartBar(Simulator.CurrentBarPosition, c);
            //CandlesChart.Append(CurBar);
            if (CurBar == null || c.Time - CurBar.Time >= new TimeSpan(0, ChartCandleWidth, 0))
            {
                CurBar = new ChartBar(c);
                CandlesChart.Items.Add(CurBar);
                //blocchiamo solo se è stata aggiunta una nuova
                if (StopOnBar && Robot.Active)
                {
                    if (StepCounter++ == ChartCandleStep)
                    {
                        StepCounter = 0;
                        UpdateChart();
                        while (!Continue)
                            System.Threading.Thread.Sleep(100);
                        Continue = false;
                    }
                }
            }
            else
            {
                CurBar.Close = c.Close;

                if (CurBar.High < c.High)
                    CurBar.High = c.High;

                if (CurBar.Low > c.Low)
                    CurBar.Low = c.Low;
            }


        }

        public void UpdateChart()
        {
            PlotViewModel.Series.Clear();

            while (Robot.Drawer.Candles.Next())
            {
                this.CandlesChart.Items.Add(new ChartBar(Robot.Drawer.Candles.Tick));

            }

            PlotViewModel.Series.Add(CandlesChart);
            //BiasSign = Robot.MovingAverage900.Value - Robot.MovingAverage900.Previous > 0 ? "Positive" : "Negative";
            var lastTick = Robot.Drawer.Candles.Tick;
            //---------- ADJUST X
            PlotViewModel.Axes[0].Minimum = Robot.Drawer.Candles.Tick.Time.Subtract(TimeSpan.FromDays(2)).ToAxisDouble();
            //- new TimeSpan(lastTick.Timeframe.Ticks * ChartWidth).ToAxisDouble();
            PlotViewModel.Axes[0].Maximum = Robot.Drawer.Candles.Tick.Time.ToAxisDouble();
            PlotViewModel.Axes[0].Reset();

            //--------- ADJUST Y
            Candlestick range = new Candlestick(Robot.Drawer.Candles.Tick);
            var items = CandlesChart.Items;
            int i = items.Count;

            while ((i > -1) && (items[--i].X >= PlotViewModel.Axes[0].Minimum))

            {
                if (range.High < items[i].High)
                    range.High = (float)items[i].High;
                if (range.Low > items[i].Low)
                    range.Low = (float)items[i].Low;
            }

            PlotViewModel.Axes[1].Minimum = range.Low;
            PlotViewModel.Axes[1].Maximum = range.High;
            PlotViewModel.Axes[1].Reset();
            //----------------- lines
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

            foreach (var line in Robot.Drawer.Lines)
            {
                LineSeries lineserie = new LineSeries()
                {
                    MarkerStrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    Color = OxyColor.FromArgb(line.ColorArgb[0], line.ColorArgb[1], line.ColorArgb[2], line.ColorArgb[3]),
                    StrokeThickness = 0.5f,
                };

                lineserie.Points.AddRange(
                    line.Points.Where(dot => ((int)dot.X) % 2 == 0).Select(dot => new DataPoint(dot.X, dot.Y))
                    );
                PlotViewModel.Series.Add(lineserie);
                linesAdded++;
            }



            //-------punti


            var pointsSerie = new OxyPlot.Series.ScatterSeries() { MarkerSize = 15, MarkerType = MarkerType.Circle };
            for (int p = 0; p < Robot.Drawer.Points.Count; p++)
                pointsSerie.Points.Add(
                    new ScatterPoint(Robot.Drawer.Points[p].Time.ToAxisDouble(), Robot.Drawer.Points[p].Value, 5));
            PlotViewModel.Series.Add(pointsSerie);

            PlotViewModel.InvalidatePlot(true);
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
            : base(c.OpenTime.ToAxisDouble(), c.High, c.Low, c.Open, c.Close)
        {
            Time = c.Time;
        }
    }
}
