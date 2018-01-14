using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class GraphDrawer
    {
        public List<(DateTime Time, double Value)> Points { get; set; } = new List<(DateTime Time, double Value)>();
        public List<Line> Lines { get; set; } = new List<Line>();
        public List<double> HorizontalLines { get; set; } = new List<double>();
        public List<double> VerticalLines { get; set; } = new List<double>();
        public TimeSerieNavigator<ICandlestick> Candles { get; set; } = new TimeSerieNavigator<ICandlestick>();
    }
    public class Line
    {
        public List<Point> Points = new List<Point>();
        public byte[] ColorArgb = new byte[4];
    }

    public struct Point
    {
        public Point(double x, double y) { X = x; Y = y; }
        public double X;
        public double Y;
    }
}
