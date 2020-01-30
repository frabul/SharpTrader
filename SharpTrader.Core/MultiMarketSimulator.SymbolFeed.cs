using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SymbolsTable = System.Collections.Generic.Dictionary<string, SharpTrader.SymbolInfo>;
#pragma warning disable CS1998
using NLog;
namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        public IEnumerable<ITrade> Trades => Markets.SelectMany(m => m.Trades);

        public class Market : IMarketApi
        {
            object LockObject = new object();
            //private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();
            private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
            private List<Trade> _Trades = new List<Trade>();
            internal Dictionary<string, SymbolFeed> SymbolsFeeds = new Dictionary<string, SymbolFeed>();
            private List<Order> PendingOrders = new List<Order>();
            private List<Order> ClosedOrders = new List<Order>();
            private List<ITrade> TradesToSignal = new List<ITrade>();
            private SymbolsTable SymbolsTable;
            private Logger Logger;

            public string MarketName { get; private set; }
            public decimal MakerFee { get; set; } = 0.00075m;
            public decimal TakerFee { get; set; } = 0.00075m;

            /// <summary>
            /// Initial spread for all symbols
            /// </summary>
            public double Spread { get; set; }
            public bool AllowBorrow { get; set; } = false;

            public DateTime Time { get; internal set; }
            public event Action<IMarketApi, ITrade> OnNewTrade;
            public IEnumerable<ISymbolFeed> Feeds => SymbolsFeeds.Values;
            public IEnumerable<ITrade> Trades => this._Trades;

            public Market(string name, decimal makerFee, decimal takerFee, string dataDir)
            {
                Logger = LogManager.GetCurrentClassLogger();

                MarketName = name;
                MakerFee = makerFee;
                TakerFee = takerFee;
                var text = System.IO.File.ReadAllText(dataDir + name + "SymbolsTable.json");
                SymbolsTable = Newtonsoft.Json.JsonConvert.DeserializeObject<SymbolsTable>(text);
            }

            public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol, DateTime warmup)
            {
                throw new NotImplementedException();
            }

            public Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
            {
                var feedFound = SymbolsFeeds.TryGetValue(symbol, out SymbolFeed feed);
                if (!feedFound)
                {
                    var sInfo = SymbolsTable[symbol];
                    feed = new SymbolFeed(this.MarketName, sInfo);
                    lock (LockObject)
                        SymbolsFeeds.Add(symbol, feed);
                }
                if (!_Balances.ContainsKey(feed.Symbol.Asset))
                    _Balances.Add(feed.Symbol.Asset, new AssetBalance());
                if (!_Balances.ContainsKey(feed.Symbol.QuoteAsset))
                    _Balances.Add(feed.Symbol.QuoteAsset, new AssetBalance());

                return Task.FromResult<ISymbolFeed>(feed);
            }

            public async Task<IMarketOperation<IOrder>> LimitOrderAsync(string symbol, TradeDirection type, decimal amount, decimal rate, string clientOrderId = null)
            {
                var order = new Order(this.MarketName, symbol, Time, type, OrderType.Limit, amount, (double)rate, clientOrderId);

                var res = RegisterOrder(order);
                if (res.result)
                {
                    lock (LockObject)
                        this.PendingOrders.Add(order);
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };
                }
                else
                {
                    return new MarketOperation<IOrder>(MarketOperationStatus.Failed, null) { ErrorInfo = res.error };
                }


            }

            private (bool result, string error) RegisterOrder(Order order)
            {
                var ass = SymbolsTable[order.Symbol];
                AssetBalance bal;
                decimal amount;
                if (order.TradeType == TradeDirection.Sell)
                {
                    bal = _Balances[ass.Asset];
                    amount = order.Amount;
                }
                else
                {
                    bal = _Balances[ass.QuoteAsset];
                    amount = order.Amount * (decimal)order.Price;
                }

                if (!AllowBorrow && bal.Free < amount)
                {
                    return (false, "Insufficient balance");
                }

                bal.Free -= amount;
                bal.Locked += amount;
                return (true, null);

            }

            public async Task<IMarketOperation<IOrder>> MarketOrderAsync(string symbol, TradeDirection type, decimal amount, string clientOrderId = null)
            {
                lock (LockObject)
                {
                    var feed = SymbolsFeeds[symbol];
                    var price = type == TradeDirection.Buy ? feed.Ask : feed.Bid;
                    var order = new Order(this.MarketName, symbol, Time, type, OrderType.Market, amount, price, clientOrderId);

                    var (result, error) = RegisterOrder(order);
                    if (!result)
                        return new MarketOperation<IOrder>(MarketOperationStatus.Failed, null) { ErrorInfo = error };

                    var trade = new Trade(
                        this.MarketName, symbol, this.Time,
                        type, price, amount, order);

                    RegisterTrade(feed, trade, isTaker: true);
                    this.ClosedOrders.Add(order);
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };
                }
            }

            public decimal GetFreeBalance(string asset)
            {
                _Balances.TryGetValue(asset, out var res);
                return res.Free;
            }
            public decimal GetTotalBalance(string asset)
            {
                _Balances.TryGetValue(asset, out var res);
                return res.Total;
            }

            public (string Symbol, AssetBalance bal)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();

            public IEnumerable<IOrder> OpenOrders
            {
                get
                {
                    lock (LockObject)
                        return PendingOrders.ToArray();
                }
            }

            internal void RaisePendingEvents()
            {
                List<ITrade> trades;
                lock (LockObject)
                {
                    trades = new List<ITrade>(TradesToSignal);
                    TradesToSignal.Clear();
                }
                foreach (var trade in trades)
                {
                    this.OnNewTrade?.Invoke(this, trade);
                }

                foreach (var feed in SymbolsFeeds.Values)
                    feed.RaisePendingEvents(feed);
            }

            internal void ResolveOrders()
            {
                //resolve orders/trades 
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        var feed = SymbolsFeeds[order.Symbol];
                        if (order.Type == OrderType.Limit)
                        {
                            var willBuy = (order.TradeType == TradeDirection.Buy && feed.LastTick.Low + feed.Spread <= (double)order.Price);
                            var willSell = (order.TradeType == TradeDirection.Sell && feed.LastTick.High - feed.Spread >= (double)order.Price);

                            if (willBuy || willSell)
                            {
                                var trade = new Trade(
                                    market: this.MarketName,
                                    symbol: feed.Symbol.Key,
                                    time: feed.LastTick.Time,
                                    price: (double)order.Price,
                                    amount: order.Amount,
                                    type: order.TradeType,
                                    order: order
                                );
                                RegisterTrade(feed, trade, isTaker: false);
                                ClosedOrders.Add(PendingOrders[i]);
                                PendingOrders.RemoveAt(i--);
                            }
                        }
                    }
                }
            }

            private void RegisterTrade(SymbolFeed feed, Trade trade, bool isTaker)
            {
                lock (LockObject)
                {

                    var qBal = _Balances[feed.Symbol.QuoteAsset];
                    var aBal = _Balances[feed.Symbol.Asset];


                    if (trade.Direction == TradeDirection.Buy)
                    {
                        aBal.Free += trade.Amount;
                        qBal.Locked -= (trade.Amount * trade.Price);
                        Debug.Assert(qBal.Locked >= 0, "incoerent trade");
                    }
                    else if (trade.Direction == TradeDirection.Sell)
                    {
                        qBal.Free += trade.Amount * trade.Price;
                        aBal.Locked -= trade.Amount;
                        Debug.Assert(_Balances[feed.Symbol.Asset].Locked >= -0.0000000001m, "incoerent trade");
                    }
                    //always pay commissions on quote asset
                    var feeRatio = isTaker ? TakerFee : MakerFee;
                    trade.Commission = trade.Amount * feeRatio * trade.Price;
                    trade.CommissionAsset = feed.Symbol.QuoteAsset;
                    qBal.Free -= trade.Commission;

                    //set order status
                    trade.Order.Status = OrderStatus.Filled;
                    trade.Order.Filled = trade.Amount;
                    this._Trades.Add(trade);
                    TradesToSignal.Add(trade);
                }
            }

            public void AddNewCandle(SymbolFeed feed, Candlestick tick)
            {
                Time = tick.CloseTime;
                feed.Spread = tick.Close * Spread;
                feed.AddNewData(tick);
            }

            public class AssetBalance
            {
                public decimal Free;
                public decimal Locked;
                public decimal Total => Free + Locked;
            }

            public Task<IMarketOperation<decimal>> GetEquity(string asset)
            {
                decimal val = 0;
                foreach (var kv in _Balances)
                {
                    if (kv.Key == asset)
                        val += kv.Value.Total;
                    else if (kv.Value.Total != 0)
                    {
                        var symbol = (kv.Key + asset);
                        if (SymbolsFeeds.ContainsKey(symbol))
                        {
                            var feed = SymbolsFeeds[symbol];
                            val += ((decimal)feed.Ask * kv.Value.Total);
                        }
                        var sym2 = asset + kv.Key;
                        if (SymbolsFeeds.ContainsKey(sym2))
                        {
                            var feed = SymbolsFeeds[sym2];
                            val += (kv.Value.Total / (decimal)feed.Bid);
                        }
                    }
                }
                return Task.FromResult<IMarketOperation<decimal>>(MarketOperation<decimal>.Completed(val));
            }

            public (decimal min, decimal step) GetMinTradable(string tradeSymbol)
            {
                return (0.00000001m, 0.00000001m);
            }

            public async Task<IMarketOperation> OrderCancelAsync(string id)
            {
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        if (order.Id == id)
                        {
                            PendingOrders.RemoveAt(i--);
                            order.Status = OrderStatus.Cancelled;

                            var ass = SymbolsTable[order.Symbol];
                            AssetBalance bal;
                            decimal amount;
                            if (order.TradeType == TradeDirection.Sell)
                            {
                                bal = _Balances[ass.Asset];
                                amount = order.Amount;
                            }
                            else
                            {
                                bal = _Balances[ass.QuoteAsset];
                                amount = order.Amount * (decimal)order.Price;
                            }


                            bal.Free += amount;
                            bal.Locked -= amount;
                            Debug.Assert(bal.Locked >= -0.0000001m, "Incoerent locked amount");
                            ClosedOrders.Add(order);
                        }
                    }
                }

                return new MarketOperation<object>(MarketOperationStatus.Completed, null);
            }


            public decimal GetSymbolPrecision(string symbol)
            {
                return 0.0000000001m;
            }

            public decimal GetMinNotional(string asset)
            {
                return 0;
            }

            internal void AddBalance(string asset, decimal amount)
            {
                if (!_Balances.ContainsKey(asset))
                    _Balances.Add(asset, new AssetBalance());
                _Balances[asset].Free += amount;
            }

            public async Task<IMarketOperation<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId)
            {
                IEnumerable<ITrade> trades;
                if (fromId != null)
                    trades = Trades.Where(t => t.Symbol == symbol && (long.Parse(t.Id) > long.Parse(fromId)));
                else
                    trades = Trades.Where(t => t.Symbol == symbol);
                return new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, trades);
            }

            public async Task<IMarketOperation<IOrder>> OrderSynchAsync(string id)
            {
                var ord = ClosedOrders.Concat(OpenOrders).Where(o => o.Id == id).FirstOrDefault();
                if (ord != null)
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, ord);
                else
                    return new MarketOperation<IOrder>(MarketOperationStatus.Failed, ord);
            }

            public IEnumerable<SymbolInfo> GetSymbols()
            {
                return SymbolsTable.Values;
            }

            public void DisposeFeed(ISymbolFeed feed)
            {

            }
        }

        public decimal GetEquity(string baseAsset)
        {
            return Markets.Sum(m => m.GetEquity(baseAsset).Result.Result);
        }

        public class SymbolFeed : ISymbolFeed
        {
            public event Action<ISymbolFeed, IBaseData> OnData;

            private TimeSpan Resolution = TimeSpan.FromSeconds(60);

            private bool onTickPending = false;

            public SymbolInfo Symbol { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public ISymbolHistory DataSource { get; set; }
            public IBaseData LastTick { get; private set; }

            public SymbolFeed(string market, SymbolInfo symbol)
            {
                this.Symbol = symbol;
                this.Market = market;
            }

            public virtual async Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime)
            {
                TimeSerie<ITradeBar> newNavigator = new TimeSerie<ITradeBar>();
                //consolidate the currently available data
                using (var navigator = new TimeSerieNavigator<ITradeBar>(this.DataSource.Ticks))
                {
                    //add all records up to current time
                    navigator.SeekNearestBefore(historyStartTime);
                    while (navigator.Next() && navigator.Time <= this.DataSource.Ticks.Time)
                    {
                        newNavigator.AddRecord(navigator.Tick);
                    }
                }
                return newNavigator;
            }
            List<IBaseData> NewData = new List<IBaseData>(10);
            internal void AddNewData(IBaseData newMarketData)
            {
                Bid = newMarketData.Value;
                Ask = Bid + Spread;
                NewData.Add(newMarketData);

            }

            public void RaisePendingEvents(ISymbolFeed sender)
            {
                if (NewData.Count > 0)
                {
                    LastTick = NewData[NewData.Count - 1];
                    foreach (var data in NewData)
                    {
                        OnData?.Invoke(this, data);

                    }
                    NewData.Clear();
                }
            }

        }

        class Order : IOrder
        {
            private static int idCounter = 0;
            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public decimal Price { get; private set; }
            public decimal Amount { get; private set; }
            public string Id { get; private set; }
            public string ClientId { get; private set; }
            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;

            public TradeDirection TradeType { get; private set; }
            public OrderType Type { get; private set; }
            public decimal Filled { get; set; }
            public DateTime Time { get; set; }
            public Order(string market, string symbol, DateTime time, TradeDirection tradeSide, OrderType orderType, decimal amount, double rate, string clientId)
            {
                Id = (idCounter++).ToString();
                ClientId = clientId;
                Symbol = symbol;
                Market = market;
                TradeType = tradeSide;
                Type = orderType;
                Amount = amount;
                Price = (decimal)rate;
                Time = time;
            }
        }

        class Trade : ITrade
        {
            private static long IdCounter = 0;
            public Trade(string market, string symbol, DateTime time, TradeDirection type, double price, decimal amount, Order order)
            {
                Market = market;
                Symbol = symbol;
                Time = time;
                Direction = type;
                Price = (decimal)price;
                Amount = amount;
                Order = order;
                Id = (IdCounter++).ToString();
            }
            public string Id { get; private set; }
            public decimal Amount { get; private set; }

            public DateTime Time { get; private set; }

            /// <summary>
            /// Commission paid
            /// </summary>
            public decimal Commission { get; set; }
            /// <summary>
            /// Asset used to pay the commission
            /// </summary>
            public string CommissionAsset { get; set; }
            public string Market { get; private set; }

            public decimal Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeDirection Direction { get; private set; }

            public Order Order { get; private set; }

            public string ClientOrderId => Order.ClientId;

            public string OrderId => Order.Id;
        }

        class MarketOperation<T> : IMarketOperation<T>
        {
            public MarketOperationStatus Status { get; internal set; }
            public T Result { get; }
            public string ErrorInfo { get; internal set; }

            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }

            public static IMarketOperation<T> Completed(T val)
            {
                return new MarketOperation<T>(MarketOperationStatus.Completed, val);
            }
        }

        public class MarketConfiguration
        {
            public string MarketName { get; set; }
            public decimal MakerFee { get; set; }
            public decimal TakerFee { get; set; }
        }
    }
}
