using System;

namespace SharpTrader
{
    public interface ITradeBar : IBaseData, ITimeRecord
    {
        DateTime OpenTime { get; }
        DateTime CloseTime { get; }
        double Open { get; }
        double Close { get; }
        double QuoteAssetVolume { get; }
        TimeSpan Timeframe { get; }
    }

    public interface IBaseData : ITimeRecord
    {
        double Low { get; }
        double High { get; }
        double Value { get; }
        MarketDataKind Kind { get; }
    }

    public enum MarketDataKind
    {
        /// Base market data type
        Base,
        /// TradeBar market data type (OHLC summary bar)
        TradeBar,
        /// Tick market data type (price-time pair)
        Tick,
        QuoteTick,
    }
}