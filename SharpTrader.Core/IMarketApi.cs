using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface IMarketApi
    {
         string MarketName { get; }
        /// <summary>
        /// Current date and time
        /// </summary>
        DateTime Time { get; }

        /// <summary>
        /// Put a market order
        /// </summary> 
        IMarketOperation MarketOrder(string symbol, TradeType type, double amount);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        IMarketOperation LimitOrder(string symbol, TradeType type, double amount, double rate);

        /// <summary>
        /// Subscribes to updates from a given symbol in a given market
        /// </summary> 
        ISymbolFeed GetSymbolFeed(string symbol);

    }

    public interface ISymbolFeed
    {
        event Action<ISymbolFeed> OnTick;
        
        string Symbol { get; }
        string Market { get; }

        double Spread { get; }
        double Bid { get; }
        double Ask { get; }
        double Volume24H { get; }
        TimeSerie<ICandlestick> GetChartData(TimeSpan timeframe);
        void SubscribeToNewCandle(IChartDataListener subscriber, TimeSpan timeframe);
    }

    public interface IChartDataListener
    {
        void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);
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
