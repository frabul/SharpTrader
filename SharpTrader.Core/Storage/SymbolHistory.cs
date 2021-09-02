using IBApi;
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
        internal SymbolHistory(HistoryView raw, DateTime startOfData, DateTime endOfData)
        {
            Market = raw.Id.Market;
            Symbol = raw.Id.Symbol;
            Resolution = raw.Id.Resolution;
            Ticks = new TimeSerieNavigator<ITradeBar>(raw.GetNavigatorFromTicks(t => t.Time > startOfData && t.Time <= endOfData));
        }

        internal SymbolHistory(HistoryView raw)
        {
            Market = raw.Id.Market;
            Symbol = raw.Id.Symbol;
            Resolution = raw.Id.Resolution;
            Ticks = new TimeSerieNavigator<ITradeBar>(raw.GetNavigatorFromTicks());
        }
    }
}
