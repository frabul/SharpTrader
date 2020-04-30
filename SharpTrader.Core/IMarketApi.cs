using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface IMarketApi : IObjectSerializationProvider
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
        Task<IRequest<IOrder>> MarketOrderAsync(string symbol, TradeDirection type, decimal amount, string clientOrderId = null);

        /// <summary>
        /// Puts a limit order on the market
        /// </summary> 
        Task<IRequest<IOrder>> LimitOrderAsync(string symbol, TradeDirection type, decimal amount, decimal rate, string clientOrderId = null);

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

        Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId);

        Task<IRequest<IOrder>> OrderSynchAsync(string id);

        Task<IRequest> OrderCancelAsync(string id);

        decimal GetFreeBalance(string asset);

        decimal GetTotalBalance(string asset);

        Task<IRequest<decimal>> GetEquity(string asset);

        (decimal min, decimal step) GetMinTradable(string tradeSymbol);

        decimal GetSymbolPrecision(string symbol);

        decimal GetMinNotional(string asset);

        void DisposeFeed(ISymbolFeed feed);
        ITrade GetTradeById(string tradeId);
        IOrder GetOrderById(string asString);
    }
}
