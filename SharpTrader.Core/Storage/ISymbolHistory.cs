using System;

namespace SharpTrader.Storage
{
    public interface ISymbolHistory
    {
        string Market { get; }
        string Symbol { get; }
        TimeSpan Resolution { get; }
        TimeSerieNavigator<ITradeBar> Ticks { get; }
    }

}
