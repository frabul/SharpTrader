using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface IMarketApi
    {
        /// <summary>
        /// Current date and time
        /// </summary>
        DateTime Time { get; }
         
        /// <summary>
        /// Put a market order
        /// </summary> 
        IMarketOperation MarketOrder(string market, string symbol, TradeType type, double amount);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        IMarketOperation LimitOrder(string market, string symbol, TradeType type, double amount, double rate);

        /// <summary>
        /// Returns available markets
        /// </summary>
        IEnumerable<string> Markets { get; }

        /// <summary>
        /// Returns available symbols for a given market
        /// </summary>
        IEnumerable<string> GetSymbols(string market);

        /// <summary>
        /// Subscribes to updates from a given symbol in a given market
        /// </summary> 
        ISymbolFeed GetSymbolFeed(string market, string symbol);

    }

    public interface ISymbolFeed
    {
        event Action<ISymbolFeed> OnTick;
        event Action<ISymbolFeed> NewCandle;
        string Symbol { get; }
        string Market { get; }

        double Spread { get; }
        double Bid { get; }
        double Ask { get; }
        double Volume24H { get; }
        Candle[] GetChartData(TimeSpan timeframe);

    }

    public interface IMarketOperation
    {
        MarketOperationStatus Status { get; }
    }
    public enum MarketOperationStatus
    {
        Completed,
        Failed,
    }
}
