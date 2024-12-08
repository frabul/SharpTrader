using LiteDB;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IBApi;
using NLog;

namespace SharpTrader.BrokersApi.InteractiveBrokers
{
    public partial class InteractiveBrokersApi : IMarketApi
    { 
        public EClientSocket Client { get; }

        public string MarketName => "InteractiveBrokers";

        public DateTime Time { get; private set; }

        public IEnumerable<IOrder> OpenOrders => throw new NotImplementedException();

        public IEnumerable<ITrade> Trades => throw new NotImplementedException();

        public event Action<IMarketApi, ITrade> OnNewTrade;
         
        public InteractiveBrokersApi()
        {
            Logger = LogManager.GetLogger("IbApi");
            EReaderSignal = new EReaderMonitorSignal();
            Client = new EClientSocket(this, EReaderSignal); 
        }

        public void DisposeFeed(ISymbolFeed feed)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<decimal>> GetEquity(string asset)
        {
            throw new NotImplementedException();
        }

        public decimal GetFreeBalance(string asset)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId)
        {
            throw new NotImplementedException();
        }

        public decimal GetMinNotional(string asset)
        {
            throw new NotImplementedException();
        }

        public (decimal min, decimal step) GetMinTradable(string tradeSymbol)
        {
            throw new NotImplementedException();
        }

        public IOrder GetOrderById(string asString)
        {
            throw new NotImplementedException();
        }

        public Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
        {
            throw new NotImplementedException();
        }

        public decimal GetSymbolPrecision(string symbol)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<SharpTrader.SymbolInfo> GetSymbols()
        {
            throw new NotImplementedException();
        }

        public decimal GetTotalBalance(string asset)
        {
            throw new NotImplementedException();
        }

        public ITrade GetTradeById(string tradeId)
        {
            throw new NotImplementedException();
        }
         
        public Task<IRequest> OrderCancelAsync(string id)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IOrder>> OrderSynchAsync(string id)
        {
            throw new NotImplementedException();
        }

        public void RegisterCustomSerializers(BsonMapper mapper)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IOrder>> MarketOrderAsync(string symbol, TradeDirection type, decimal amount, string clientOrderId = null, TimeInForce timeInForce = TimeInForce.GTC)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IOrder>> LimitOrderAsync(string symbol, TradeDirection type, decimal amount, decimal rate, string clientOrderId = null, TimeInForce timeInForce = TimeInForce.GTC)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, DateTime fromTime)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(DateTime fromTime)
        {
            throw new NotImplementedException();
        }

        public Task<IRequest<IOrder>> PostNewOrder(OrderInfo orderInfo)
        {
            throw new NotImplementedException();
        }

        public SharpTrader.SymbolInfo GetSymbolInfo(string asString)
        {
            throw new NotImplementedException();
        }
    }
}
