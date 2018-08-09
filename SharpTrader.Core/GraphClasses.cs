using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class PlotHelper
    {
        private object Locker = new object();
        public string Title { get; set; }
        public List<(DateTime Time, double Value)> Points { get; set; } = new List<(DateTime Time, double Value)>();
        public List<Line> Lines { get; set; } = new List<Line>();
        public List<Line> LinesOnDedicatedAxis = new List<Line>();

        public List<double> HorizontalLines { get; set; } = new List<double>();
        public List<double> VerticalLines { get; set; } = new List<double>();
        public TimeSerieNavigator<ICandlestick> Candles { get; set; } = new TimeSerieNavigator<ICandlestick>();
        List<ITrade> TradesToAdd { get; } = new List<ITrade>();

        public void PlotTrade(ITrade newTrade)
        {
            lock (Locker)
            {
                if (newTrade.Type == TradeType.Buy)
                {
                    TradesToAdd.Add(newTrade);
                }
                else
                {
                    var arrival = new Point(newTrade.Time, (double)newTrade.Price);
                    foreach (var tr in TradesToAdd)
                    {
                        var start = new Point(tr.Time, (double)tr.Price);
                        if (tr.Price == 0)
                            Console.WriteLine("price 0");
                        var color = arrival.Y * (1 - 0.002) > start.Y ?
                            new ColorARGB(255, 10, 10, 255) : new ColorARGB(255, 255, 10, 10);
                        this.Lines.Add(new Line() { Color = color, Points = new List<Point>() { start, arrival } });
                    }
                    TradesToAdd.Clear();
                }
            }

            //if it is sell trade add lines from the last not signaled trades to this  
        }

        public void PlotLine(List<(DateTime time, double value)> values, ColorARGB color, bool dedicatedAxis = false)
        {
            var chartLine = new Line() { Color = color };

            if (!dedicatedAxis)
                Lines.Add(chartLine);
            else
                LinesOnDedicatedAxis.Add(chartLine);

            foreach (var val in values)
                chartLine.Points.Add(new Point(val.time, (double)val.value));
        }

        public void PlotLine<T>(TimeSerieNavigator<T> timeSerie,
                                ColorARGB color,
                                Func<T, double> valuesSelector, bool dedicatedAxis = false) where T : ITimeRecord
        {
            var myNavigator = new TimeSerieNavigator<T>(timeSerie);
            var chartLine = new Line() { Color = color };

            if (!dedicatedAxis)
                Lines.Add(chartLine);
            else
                LinesOnDedicatedAxis.Add(chartLine);
            myNavigator.OnNewRecord += (T obj) =>
            {
                while (myNavigator.Next())
                {
                    var value = valuesSelector(myNavigator.Tick);

                    chartLine.Points.Add(new Point(myNavigator.Tick.Time, value));

                }
            };
        }


        public void PlotLines<T>(TimeSerieNavigator<T> timeSerie,
                                 ColorARGB color,
                                 Func<T, double[]> valuesSelector, bool dedicatedAxis = false) where T : ITimeRecord
        {
            var myNavigator = new TimeSerieNavigator<T>(timeSerie);
            List<Line> lines = new List<Line>();
            bool firstPass = true;
            myNavigator.OnNewRecord += (T obj) =>
            {
                while (myNavigator.Next())
                {
                    var values = valuesSelector(myNavigator.Tick);
                    if (firstPass)
                    {
                        firstPass = false;
                        foreach (var val in values)
                            lines.Add(new Line() { Color = color });
                        if (!dedicatedAxis)
                            Lines.AddRange(lines);
                        else
                            LinesOnDedicatedAxis.AddRange(lines);
                    }
                    for (int i = 0; i < values.Length; i++)
                    {
                        lines[i].Points.Add(new Point(myNavigator.Tick.Time, values[i]));
                    }
                }
            };
        }

        public void PlotOperation(ITrade buy, ITrade sell)
        {
            var p1 = new Point(buy.Time, (double)buy.Price);
            var p2 = new Point(sell.Time, (double)sell.Price);
            var color = buy.Price < sell.Price ?
                new ColorARGB(255, 10, 255, 10) : new ColorARGB(255, 255, 10, 90);
            this.Lines.Add(new Line() { Color = color, Points = new List<Point>() { p1, p2 } });
        }
    }

    public class Line
    {
        public List<Point> Points = new List<Point>();
        public ColorARGB Color;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ColorARGB
    {
        [FieldOffset(0)]
        public byte A;
        [FieldOffset(1)]
        public byte R;
        [FieldOffset(2)]
        public byte G;
        [FieldOffset(3)]
        public byte B;
        [FieldOffset(0)]
        public uint Value;

        public ColorARGB(byte a, byte r, byte g, byte b) : this()
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }
    }
    public struct Point
    {
        public Point(DateTime x, double y) { X = x; Y = y; }
        public DateTime X { get; }
        public double Y { get; }
    }
}
