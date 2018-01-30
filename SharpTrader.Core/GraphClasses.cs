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
        public List<(DateTime Time, double Value)> Points { get; set; } = new List<(DateTime Time, double Value)>();
        public List<Line> Lines { get; set; } = new List<Line>();
        public List<double> HorizontalLines { get; set; } = new List<double>();
        public List<double> VerticalLines { get; set; } = new List<double>();
        public TimeSerieNavigator<ICandlestick> Candles { get; set; } = new TimeSerieNavigator<ICandlestick>();
        public string Title { get; set; }

        public void PlotLines<T>(TimeSerieNavigator<T> timeSerie,
                                 ColorARGB color,
                                 Func<T, double[]> valuesSelector) where T : ITimeRecord
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
                        Lines.AddRange(lines);
                    }
                    for (int i = 0; i < values.Length; i++)
                    {
                        lines[i].Points.Add(new Point(myNavigator.Tick.Time, values[i]));
                    }
                }
            };
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
