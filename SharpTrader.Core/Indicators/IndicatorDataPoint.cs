using System;

namespace SharpTrader.Indicators
{
    public class IndicatorDataPoint : IBaseData
    {
        public static IndicatorDataPoint Zero { get; } = new IndicatorDataPoint(DateTime.MinValue, 0);
        public DateTime Time { get; set; }
        public virtual double Value { get; }

        public double Low => Value;

        public double High => Value;

        public MarketDataKind Kind => MarketDataKind.Tick;

        public IndicatorDataPoint(DateTime time, double value)
        {
            Value = value;
            Time = time;
        }


    }
}
