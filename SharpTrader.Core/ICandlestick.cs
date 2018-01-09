using System;

namespace SharpTrader
{
    public interface ICandlestick : ITimeRecord
    {
        DateTime OpenTime { get; }
        DateTime CloseTime { get; }
        double Open { get; }
        double High { get; }
        double Low { get; }
        double Close { get; }
        double Volume { get; }
        TimeSpan Timeframe { get; }
    }
}