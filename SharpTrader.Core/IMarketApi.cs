using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class OrderInfo
    {
        public string Symbol { get; set; }
        public OrderType Type { get; set; }
        public TradeDirection Direction { get; set; }
        public MarginOrderEffect Effect { get; set; }
        public decimal Amount { get; set; }
        public decimal Price { get; set; }
        public string ClientOrderId { get; set; }
        public TimeInForce TimeInForce { get; set; } = TimeInForce.GTC;
    }


    public interface IMarketApi : IObjectSerializationProvider
    {
        event Action<IMarketApi, ITrade> OnNewTrade;

        string MarketName { get; }
        /// <summary>
        /// Current date and time
        /// </summary>
        DateTime Time { get; }

        /// <summary>
        /// Puts an order on the market
        /// </summary> 
        Task<IRequest<IOrder>> PostNewOrder(OrderInfo orderInfo);

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
        Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, DateTime fromTime);
        Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(DateTime fromTime);
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

    public enum TimeInForce
    {
        GTC,
        IOC,
    }

    public enum MarginOrderEffect
    {
        /// <summary>
        /// Spot order
        /// </summary>
        None,
        /// <summary>
        /// Buys or sells with borrow
        /// </summary>
        OpenPosition,
        /// <summary>
        /// Buys or selles and then repay borrowed assets
        /// </summary>
        ClosePosition,
    }


}
