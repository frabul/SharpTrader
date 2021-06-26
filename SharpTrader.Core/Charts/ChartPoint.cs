using System;

namespace SharpTrader.Charts
{
#pragma warning disable IDE1006 // Naming Styles
    public struct ChartPoint
    {
        public ChartPoint(DateTime x, decimal y) { time = x; value = y; }
        public DateTime time { get; set; }
        public decimal value { get; set; }
    }
}
