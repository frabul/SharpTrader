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
        Task<IMarketOperation<IOrder>> MarketOrderAsync(string symbol, TradeType type, decimal amount, string clientOrderId = null);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        Task<IMarketOperation<IOrder>> LimitOrderAsync(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null);

        /// <summary>
        /// Gets the feed for the given symbol
        /// </summary> 
        Task<ISymbolFeed> GetSymbolFeedAsync(string symbol);

         

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

        Task<IMarketOperation<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId);
        Task<IMarketOperation<IOrder>> QueryOrderAsync(string symbol, string id);

        Task<IMarketOperation> OrderCancelAsync(string id);

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
        string Asset { get; }
        string QuoteAsset { get; }
        /// <summary>
        /// Returns maker data history ( candlesticks ) with provided timeframe 
        /// </summary>  
        Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe);
        /// <summary>
        /// Returns maker data history ( candlesticks ) with provided timeframe 
        /// </summary>  
        Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe, DateTime historyStartTime);

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
