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
        /// Gets the feed for the given symbol
        /// </summary> 
        Task<ISymbolFeed> GetSymbolFeedAsync(string symbol);

        /// <summary>
        /// Gets the real time data feed for the given symbol
        /// </summary>
        /// <param name="symbol">symbol </param>
        /// <param name="pastDataToLoad">past data to load</param>
        /// <returns></returns>
        Task<ISymbolFeed> GetSymbolFeedAsync(string symbol, DateTime warmup);

        /// <summary>
        /// Get all available symbols for in this market
        /// </summary>
        /// <returns></returns>
        IEnumerable<SymbolInfo> GetSymbols();

        /// <summary>
        /// Get all currently open orders
        /// </summary> 
        IEnumerable<IOrder> OpenOrders { get; }
        IEnumerable<ISymbolFeed> ActiveFeeds { get; }
        IEnumerable<ITrade> Trades { get; }
        IMarketOperation OrderCancel(string id);
        IMarketOperation<IEnumerable<ITrade>> GetLastTrades(string symbol, int count, string fromId);
        IMarketOperation<IOrder> QueryOrder(string symbol, string id);

        decimal GetFreeBalance(string asset);

        decimal GetEquity(string asset);
        (decimal min, decimal step) GetMinTradable(string tradeSymbol);
        decimal GetSymbolPrecision(string symbol);

        decimal GetMinNotional(string asset);

    }

    public class SymbolInfo
    {
        public string Symbol { get; set; }
        public string Asset { get; set; }
        public string QuoteAsset { get; set; }
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

        Task SetHistoryStartAsync();
    }

    public interface IChartDataListener
    {
        void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);
    }

    public interface IMarketOperation<T> : IMarketOperation
    {
        T Result { get; }
    }

    public interface IMarketOperation
    {
        MarketOperationStatus Status { get; }
        string ErrorInfo { get; }
    }

    public enum MarketOperationStatus
    {
        Completed,
        Failed,
    }
}
