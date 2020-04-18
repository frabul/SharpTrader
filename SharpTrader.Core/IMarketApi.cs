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
        Task<IMarketOperation<IOrder>> MarketOrderAsync(string symbol, TradeDirection type, decimal amount, string clientOrderId = null);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        Task<IMarketOperation<IOrder>> LimitOrderAsync(string symbol, TradeDirection type, decimal amount, decimal rate, string clientOrderId = null);

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

        IEnumerable<ITrade> Trades { get; }

        Task<IMarketOperation<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId);

        Task<IMarketOperation<IOrder>> OrderSynchAsync(string id);

        Task<IMarketOperation> OrderCancelAsync(string id);

        decimal GetFreeBalance(string asset);

        decimal GetTotalBalance(string asset);

        Task<IMarketOperation<decimal>> GetEquity(string asset);

        (decimal min, decimal step) GetMinTradable(string tradeSymbol);

        decimal GetSymbolPrecision(string symbol);

        decimal GetMinNotional(string asset);

        void DisposeFeed(ISymbolFeed feed);
    }

    public class SymbolInfo
    {
        public string Key { get; set; }
        public string Asset { get; set; }
        public string QuoteAsset { get; set; }
        public bool IsMarginTadingAllowed { get; set; }
        public bool IsSpotTadingAllowed { get; set; } 
        public decimal MinLotSize { get; set; }
        public decimal LotSizeStep { get; set; }
        public decimal MinNotional { get; set; }
        public decimal PricePrecision { get; set; }
        public bool IsBorrowAllowed { get; set; }

        public override string ToString()
        {
            return Key;
        }

        public static implicit operator string(  SymbolInfo obj)
        {
            return obj.Key;
        }
    }

    public interface ISymbolFeed
    {
        event Action<ISymbolFeed, IBaseData> OnData;
        SymbolInfo Symbol { get; }
        string Market { get; }
        double Spread { get; }
        double Bid { get; }
        double Ask { get; }
        DateTime Time { get; }

        /// <summary>
        /// Returns market data history ( candlesticks ) from give time
        /// </summary>  
        Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime);

        /// <summary>
        /// Helper method to fix the order amount ad price in order to be compliant to the symbol limits
        /// </summary> 
        /// <returns>(decimal amount, decimal price) </returns>
        (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal oderAmout, decimal exitPrice);
        /// <summary>
        /// Helper method to fix the order amount ad price in order to be compliant to the symbol limits
        /// </summary> 
        /// <returns>(decimal amount, decimal price) </returns>
        (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal oderAmout, decimal exitPrice);
    }

    public interface IMarketOperation<T> : IMarketOperation
    {
        T Result { get; }
    }

    public interface IMarketOperation
    {
        MarketOperationStatus Status { get; }
        string ErrorInfo { get; }
        bool IsSuccessful { get; }
    }

    public enum MarketOperationStatus
    {
        Completed,
        Failed,
    }
}
