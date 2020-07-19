using System;
using System.Linq;

namespace SharpTrader.Storage
{
    class SymbolHistory : ISymbolHistory
    {
        public string Market { get; }
        public string Symbol { get; }
        public TimeSpan Resolution { get; }
        public TimeSerieNavigator<ITradeBar> Ticks { get; }
        public double Spread { get; }
        public SymbolHistory(SymbolHistoryRaw raw, DateTime startOfData, DateTime endOfData)
        {
            Market = raw.Market;
            Symbol = raw.Symbol;
            Resolution = raw.Timeframe;
            Ticks = new TimeSerieNavigator<ITradeBar>(raw.Ticks.Where(t => t.Time >= startOfData && t.Time <= endOfData));
            Spread = raw.Spread;
        }
        public SymbolHistory(SymbolHistoryRaw raw)
        {
            Market = raw.Market;
            Symbol = raw.Symbol;
            Resolution = raw.Timeframe;
            Ticks = new TimeSerieNavigator<ITradeBar>(raw.Ticks);
            Spread = raw.Spread;
        }
    }
}
