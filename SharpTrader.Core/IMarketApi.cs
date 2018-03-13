using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface IMarketApi
    {
        event Action<IMarketApi, ITrade> OnNewTrade;

        string MarketName { get; }
        /// <summary>
        /// Current date and time
        /// </summary>
        DateTime Time { get; }

        /// <summary>
        /// Put a market order
        /// </summary> 
        IMarketOperation<IOrder> MarketOrder(string symbol, TradeType type, decimal amount, string clientOrderId = null);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null);

        /// <summary>
        /// Subscribes to updates from a given symbol in a given market
        /// </summary> 
        ISymbolFeed GetSymbolFeed(string symbol);

        ISymbolFeed GetSymbolFeed(string symbol, TimeSpan warmup);

        /// <summary>
        /// Get all currently open orders
        /// </summary> 
        IEnumerable<IOrder> OpenOrders { get; }

        IEnumerable<ISymbolFeed> ActiveFeeds { get; }

        IEnumerable<ITrade> Trades { get; }

        decimal GetFreeBalance(string asset);

        decimal GetEquity(string asset);
        (decimal min, decimal step) GetMinTradable(string tradeSymbol);
        decimal GetSymbolPrecision(string symbol);

        decimal GetMinNotional(string asset);

        IEnumerable<ITrade> GetLastTrades(string symbol, int count, string fromId);
        void OrderCancel(string id);
        IOrder QueryOrder(string symbol, string id);
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
        string Asset { get; }
        string QuoteAsset { get; }
        TimeSerieNavigator<ICandlestick> GetNavigator(TimeSpan timeframe);
        void SubscribeToNewCandle(IChartDataListener subscriber, TimeSpan timeframe);
    }

    public interface IChartDataListener
    {
        void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);
    }

    public interface IMarketOperation<T>
    {
        MarketOperationStatus Status { get; }
        T Result { get; }
    }
    public enum MarketOperationStatus
    {
        Completed,
        Failed,
    }
}
