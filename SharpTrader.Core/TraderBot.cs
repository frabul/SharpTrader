using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core
{
    public interface IMarketApi
    {
        bool Simulator { get; }
        MarginTrade ClosePosition(Position pos, double price = 0);
        Position OpenPosition(double amount, double entry, double takeProfit, double stopLoss); 
        void Subscribe
    }

    public abstract class TraderBot
    {
        public bool Active { get; set; } 
        public IMarketApi Market { get; }  
        public string Results { get; }
    }

    public class GraphDrawer
    {
        public List<Tuple<int, double>> Points { get; }
        public List<Line> Lines { get; }
        public List<double> HorizontalLines { get; }
        public List<double> VerticalLines { get; }
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
