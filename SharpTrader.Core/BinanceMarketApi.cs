using Binance.API.Csharp.Client;
using be = Binance.API.Csharp.Client.Models.Enums;
using Binance.API.Csharp.Client.Models.Account;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.API.Csharp.Client.Models.UserStream;
using Binance.API.Csharp.Client.Domain.Abstract;
using Binance.API.Csharp.Client.Models.WebSocket;
using Binance.API.Csharp.Client.Models.Market.TradingRules;
using SymbolsTable = System.Collections.Generic.Dictionary<string, (string Asset, string Quote)>;

using WebSocketSharp;
using System.Diagnostics;
using System.Timers;
using Newtonsoft.Json;

namespace SharpTrader
{
    public class BinanceMarketApi : IMarketApi
    {
        public event Action<IMarketApi, ITrade> OnNewTrade;

        private Stopwatch LastListenTry = new Stopwatch();
        private HistoricalRateDataBase HistoryDb = new HistoricalRateDataBase(".\\Data\\");
        private BinanceClient Client;
        private TradingRules ExchangeInfo;
        private List<NewOrder> NewOrders = new List<NewOrder>();
        private long ServerTimeDiff;
        private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();
        private string UserDataListenKey;
        private System.Timers.Timer HearthBeatTimer;
        private SymbolsTable SymbolsTable = new SymbolsTable();

        private List<SymbolFeed> Feeds = new List<SymbolFeed>();
        private object LockObject = new object();
        private List<ApiOrder> _OpenOrders = new List<ApiOrder>();
        private List<ApiOrder> Orders = new List<ApiOrder>();
        public string MarketName => "Binance";
        public bool Test { get; set; }
        public DateTime Time => DateTime.UtcNow.AddMilliseconds(ServerTimeDiff);

        public IEnumerable<ISymbolFeed> ActiveFeeds => throw new NotImplementedException();

        public IEnumerable<ITrade> Trades => throw new NotImplementedException();

        public (string Symbol, decimal balance)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();

        public IEnumerable<IOrder> OpenOrders
        {
            get
            {
                lock (LockObject)
                    return _OpenOrders.ToArray(); // Orders.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled);
            }
        }

        public BinanceMarketApi(string apiKey, string apiSecret)
        {
            Client = new BinanceClient(new ApiClient(apiKey, apiSecret));

            ServerTimeSynch();

            //Client.TestConnectivity();
            //UserDataStream = Client.StartUserStream().Result;

            ExchangeInfo = Client.GetTradingRulesAsync().Result;
            foreach (var symb in ExchangeInfo.Symbols)
            {
                SymbolsTable.Add(symb.SymbolName, (symb.BaseAsset, symb.QuoteAsset));
            }
            //download account info
            //todo Synch trades
            SynchBalance();
            ListenUserData();
            SynchOrders();
            HearthBeatTimer = new System.Timers.Timer()
            {
                Interval = 5000,
                AutoReset = false,
                Enabled = true,

            };
            HearthBeatTimer.Elapsed += HearthBeat;
        }

        private void SynchOrders()
        {
            lock (LockObject)
            {
                try
                {
                    _OpenOrders.Clear();
                    var currOrders = Client.GetCurrentOpenOrders("").Result;
                    foreach (var order in currOrders)
                    {
                        var ord = new ApiOrder(order);
                        _OpenOrders.Add(ord);
                        Orders.Add(ord);
                    }
                }
                catch
                {
                    Console.WriteLine("Error whili synch orders");
                }
            }
        }

        private void ServerTimeSynch()
        {
            try
            {
                var time = Client.GetServerTime().Result;
                var timeNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                //Console.WriteLine($"Connected to Binance:\n\t server time {time.ServerTime}\n\t local time  {timeNow} ");
                ServerTimeDiff = time.ServerTime - timeNow;

                if ((ServerTimeDiff) < 0)
                    Binance.API.Csharp.Client.Utils.Utilities.DeltaTimeAdjustment = (long)((time.ServerTime - timeNow) * 1.1);
            }
            catch
            {
                Console.WriteLine("Error during server time synch");
            }

        }

        Stopwatch BalanceUpdateWatchdog = new Stopwatch();
        Stopwatch SynchOrdersWatchdog = new Stopwatch();
        private void HearthBeat(object state, ElapsedEventArgs elapsed)
        {
            lock (LockObject)
            {
                bool connected = true;
                try
                {
                    if (UserDataListenKey != null)
                    {
                        var vari = Client.KeepAliveUserStream(UserDataListenKey).Result;
                    }
                    else
                        connected = false;
                }
                catch
                {
                    connected = false;
                }
                if (!connected)
                    ListenUserData();
            }

            SynchBalance();
            if (!SynchOrdersWatchdog.IsRunning || SynchOrdersWatchdog.ElapsedMilliseconds > 65000)
            {
                SynchOrders();
                SynchOrdersWatchdog.Restart();
            }
            HearthBeatTimer.Start();
        }

        private void SynchBalance()
        {
            if (!BalanceUpdateWatchdog.IsRunning || BalanceUpdateWatchdog.ElapsedMilliseconds > 10000)
            {
                lock (LockObject)
                {
                    ServerTimeSynch();
                    BalanceUpdateWatchdog.Restart();
                    try
                    {
                        var accountInfo = Client.GetAccountInfo().Result;
                        foreach (var bal in accountInfo.Balances)
                            this._Balances[bal.Asset] = bal.Free;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error while trying to update account info " + ex.Message);
                    }
                }
            }
        }

        private void ListenUserData()
        {
            lock (LockObject)
            {
                try
                {
                    if (UserDataListenKey != null)
                    {
                        var res = Client.CloseUserStream(UserDataListenKey).Result;
                    }
                }
                catch { }
                UserDataListenKey = null;
                try
                {
                    if (LastListenTry.IsRunning && LastListenTry.ElapsedMilliseconds < 20000)
                        return;

                    if (UserDataListenKey != null)
                    {
                        var res = Client.CloseUserStream(UserDataListenKey).Result;
                    }

                    UserDataListenKey = Client.ListenUserDataEndpoint(
                        HandleAccountUpdatedMessage,
                        HandleTradeUpdateMsg,
                        HandleOrderUpdateMsg);
                }
                catch
                {
                    Console.WriteLine("Failed to listen for user data stream.");
                }
                LastListenTry.Restart();
            }

        }

        private void HandleAccountUpdatedMessage(AccountUpdatedMessage msg)
        {
            lock (LockObject)
            {
                foreach (var bal in msg.Balances)
                {
                    this._Balances[bal.Asset] = bal.Free;
                }
            }

        }

        private void HandleOrderUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {
            lock (LockObject)
            {
                var order = new ApiOrder(msg);
                bool remove = true;
                if (msg.Status == "NEW" || msg.Status == "PARTIALLY_FILLED")
                    remove = false;//order open
                //update and add
                bool found = false;
                for (int i = 0; i < Orders.Count; i++)
                {
                    if (Orders[i].BinanceOrder.OrderId == msg.OrderId)
                    {
                        Orders[i].Update(order);
                        found = true;
                    }
                }
                if (!found)
                    Orders.Add(order);
                //-----
                found = false;
                for (int i = 0; i < _OpenOrders.Count; i++)
                {
                    if (_OpenOrders[i].BinanceOrder.OrderId == msg.OrderId)
                    {
                        if (remove)
                            _OpenOrders.RemoveAt(i);
                        found = true;
                    }
                }
                if (!found && !remove)
                {
                    _OpenOrders.Add(order);
                }
            }
        }

        private void HandleTradeUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {
            var trade = new ApiTrade(msg);
            lock (LockObject)
            {
                var order = Orders.FirstOrDefault(o => o.Id == trade.OrderId);
                if (order != null)
                {
                    if (msg.ExecutionType == "CANCELLED")
                    {
                        Orders.Remove(order);
                        if (_OpenOrders.Contains(order))
                            _OpenOrders.Remove(order);
                    }
                    order.ResultingTrades.Add(trade);
                }
            }

            OnNewTrade?.Invoke(this, trade);
        }

        public decimal GetBalance(string asset)
        {
            lock (LockObject)
            {
                if (_Balances.ContainsKey(asset))
                    return _Balances[asset];

            }
            return 0;
        }

        public decimal GetNormalizedPortfolioValue(string asset)
        {
            throw new NotImplementedException();
        }

        public ISymbolFeed GetSymbolFeed(string symbol)
        {
            var feed = Feeds.Where(sf => sf.Symbol == symbol).FirstOrDefault();
            if (feed == null)
            {
                var (Asset, Quote) = SymbolsTable[symbol];
                feed = new SymbolFeed(Client, HistoryDb, MarketName, symbol, Asset, Quote);
                Feeds.Add(feed);
            }
            return feed;
        }

        public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate)
        {
            lock (LockObject)
            {
                var side = type == TradeType.Buy ? be.OrderSide.BUY : be.OrderSide.SELL;
                var order = Client.PostNewOrder(symbol, amount, rate, side, be.OrderType.LIMIT).Result;
                var apiOrder = Orders.First(o => o.Id == order.OrderId.ToString());
                if (apiOrder == null)
                    apiOrder = new ApiOrder(order);
                else
                    Console.WriteLine($"Two different orders: { JsonConvert.SerializeObject(apiOrder)} - { JsonConvert.SerializeObject(order)}");

                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, apiOrder);

            }



        }

        public IMarketOperation<IOrder> MarketOrder(string symbol, TradeType type, decimal amount)
        {
            var side = type == TradeType.Buy ? be.OrderSide.BUY : be.OrderSide.SELL;
            try
            {
                dynamic res;
                ApiOrder apiOrder = null;
                lock (LockObject)
                {
                    if (Test)
                    {
                        res = Client.PostNewOrderTest(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                    }
                    else
                    {
                        var ord = Client.PostNewOrder(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                        apiOrder = new ApiOrder(ord);
                        NewOrders.Add(ord);
                       
                    }
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, apiOrder);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Market operation failed because: " + ex.InnerException.Message);
                return new MarketOperation<IOrder>(MarketOperationStatus.Failed, null);
            }
        }

        public (decimal min, decimal step) GetMinTradable(string tradeSymbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.SymbolName == tradeSymbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == "LOT_SIZE").FirstOrDefault();
                if (filt != null)
                    return (filt.MinQty, filt.StepSize);
            }
            return (0, 0);
        }

        public decimal GetSymbolPrecision(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.SymbolName == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == "PRICE_FILTER").FirstOrDefault();
                if (filt != null)
                    return filt.TickSize;
            }
            return 0.000001m;
        }

        public void OrderCancel(string id)
        {
            var orders = this.Orders.Where(or => or.Id == id);
            Debug.Assert(orders.Count() < 2, "Two orders with same id");
            var order = orders.FirstOrDefault();
            if (order == null)
                throw new Exception($"Order {id} not found");

            var cancel = this.Client.CancelOrder(order.Symbol, order.BinanceOrder.OrderId, recvWindow: 5000).Result;

        }

        public decimal GetMinNotional(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.SymbolName == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == "MIN_NOTIONAL").FirstOrDefault();
                if (filt != null)
                    return filt.MinNotional;
            }
            return 0;
        }

        class MarketOperation<T> : IMarketOperation<T>
        {
            public MarketOperation(MarketOperationStatus status)
            {
                Status = status;

            }
            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }
            public MarketOperationStatus Status { get; internal set; }

            public T Result { get; }
        }

        class SymbolFeed : SymbolFeedBoilerplate, ISymbolFeed
        {
            private BinanceClient Client;
            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            private HistoricalRateDataBase HistoryDb;
            private object Locker = new object();
            private WebSocket KlineSocket;
            private WebSocket PartialDepthSocket;

            private Stopwatch KlineWatchdog = new Stopwatch();
            private Stopwatch PartialDepthWatchDog = new Stopwatch();
            private Timer HearthBeatTimer;

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string QuoteAsset { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }

            public SymbolFeed(BinanceClient client, HistoricalRateDataBase hist, string market, string symbol, string asset, string quoteAsset)
            {
                HistoryDb = hist;
                this.Client = client;
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
                //load missing data to hist db 
                Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");
                var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb);
                downloader.DownloadCompleteSymbolHistory(Symbol);

                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(this.Market, Symbol, TimeSpan.FromSeconds(60));

                while (symbolHistory.Ticks.Next())
                    this.Ticks.AddRecord(symbolHistory.Ticks.Tick);

                PartialDepthListen(null);
                KlineListen(null);


                HearthBeatTimer = new Timer(5000)
                {
                    AutoReset = false,
                    Enabled = true,

                };
                HearthBeatTimer.Elapsed += HearthBeat;
            }


            void HearthBeat(object state, ElapsedEventArgs args)
            {
                if (KlineWatchdog.ElapsedMilliseconds > 60000)
                {
                    var res = KlineSocket.Ping();
                    if (!res)
                        KlineListen(null);
                }
                if (PartialDepthWatchDog.ElapsedMilliseconds > 10000)
                {
                    var res = PartialDepthSocket.Ping();
                    if (!res)
                        PartialDepthListen(null);
                }
                HearthBeatTimer.Start();
            }

            void KlineListen(CloseEventArgs eventArgs)
            {
                try
                {
                    if (KlineSocket != null)
                        KlineSocket.Close();
                }
                catch { }

                KlineSocket = Client.ListenKlineEndpoint(this.Symbol.ToLower(), be.TimeInterval.Minutes_1, HandleKlineEvent);
                KlineWatchdog.Restart();
            }
            void PartialDepthListen(CloseEventArgs eventArgs)
            {
                try
                {
                    if (PartialDepthSocket != null)
                        PartialDepthSocket.Close();
                }
                catch { }
                PartialDepthSocket = Client.ListenPartialDepthEndPoint(this.Symbol.ToLower(), 5, HandleDepthUpdate);
                PartialDepthWatchDog.Restart();
            }

            private void HandleDepthUpdate(DepthPartialMessage messageData)
            {
                PartialDepthWatchDog.Restart();
                var bid = (double)messageData.Bids.FirstOrDefault().Price;
                var ask = (double)messageData.Asks.FirstOrDefault().Price;
                if (bid != 0 && ask != 0)
                {
                    this.Bid = bid;
                    this.Ask = ask;
                    Spread = Ask - Bid;
                    SignalTick();
                }

            }

            private void HandleKlineEvent(KlineMessage msg)
            {
                KlineWatchdog.Restart();
                this.Bid = (double)msg.KlineInfo.Close;

                if (msg.KlineInfo.IsFinal)
                {
                    var ki = msg.KlineInfo;
                    var candle = new Candlestick()
                    {
                        Close = (double)msg.KlineInfo.Close,
                        High = (double)msg.KlineInfo.High,
                        CloseTime = (msg.KlineInfo.EndTime + 1).ToDatetimeMilliseconds(),
                        OpenTime = msg.KlineInfo.StartTime.ToDatetimeMilliseconds(),
                        Low = (double)msg.KlineInfo.Low,
                        Open = (double)msg.KlineInfo.Open,
                        Volume = (double)msg.KlineInfo.Volume
                    };
                    BaseTimeframe = candle.CloseTime - candle.OpenTime;
                    Ticks.AddRecord(candle);
                    UpdateDerivedCharts(candle);
                    SignalTick();
                }
                RaisePendingEvents(this);
            }
        }

        class ApiOrder : IOrder
        {
            private OrderOrTradeUpdatedMessage msg;

            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public double Rate { get; private set; }
            public decimal Amount { get; private set; }
            public string Id { get; private set; }
            public TradeType TradeType { get; private set; }
            public OrderType Type { get; private set; }
            public Order BinanceOrder { get; set; }
            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
            internal List<ApiTrade> ResultingTrades { get; private set; } = new List<ApiTrade>();
            public IEnumerable<ITrade> Trades => ResultingTrades;

            public decimal Filled { get; private set; }

            public ApiOrder(NewOrder binanceOrder)
            {
                Symbol = binanceOrder.Symbol;
                Market = "Binance";
                Id = binanceOrder.OrderId.ToString();
            }

            public ApiOrder(Order binanceOrder)
            {
                Symbol = binanceOrder.Symbol;
                Market = "Binance";
                TradeType = binanceOrder.Side == "BUY" ? TradeType.Buy : TradeType.Sell;
                Type = binanceOrder.Type == "LIMIT" ? OrderType.Limit : OrderType.Market;
                Amount = binanceOrder.OrigQty;
                Rate = (double)binanceOrder.Price;
                Status = GetStatus(binanceOrder.Status);
                Id = binanceOrder.OrderId.ToString();
                Filled = binanceOrder.ExecutedQty;
                BinanceOrder = binanceOrder;
            }

            public ApiOrder(OrderOrTradeUpdatedMessage bo)
            {
                Symbol = bo.Symbol;
                Market = "Binance";
                TradeType = bo.Side == "BUY" ? TradeType.Buy : TradeType.Sell;
                Type = bo.Type == "LIMIT" ? OrderType.Limit : OrderType.Market;
                Amount = bo.OriginalQuantity;
                Rate = (double)bo.Price;
                Status = GetStatus(bo.Status);
                Id = bo.OrderId.ToString();
                Filled = bo.FilledTradesAccumulatedQuantity;
                BinanceOrder = new Order
                {
                    ClientOrderId = bo.NewClientOrderId,
                    ExecutedQty = bo.FilledTradesAccumulatedQuantity,
                    IcebergQty = bo.IcebergQuantity,
                    OrderId = bo.OrderId,
                    OrigQty = bo.OriginalQuantity,
                    Price = bo.Price,
                    Side = bo.Side,
                    Status = bo.Status,
                    StopPrice = bo.StopPrice,
                    Symbol = bo.Symbol,
                    Time = bo.TradeTime,
                    TimeInForce = bo.TimeInForce,
                    Type = bo.Type,

                };
            }


            internal static OrderStatus GetStatus(string status)
            {
                switch (status)
                {
                    case "NEW":
                        return OrderStatus.Pending;
                    case "PARTIALLY_FILLED":
                        return OrderStatus.PartiallyFilled;
                    case "FILLED":
                        return OrderStatus.Filled;
                    case "CANCELED":
                        return OrderStatus.Cancelled;
                    case "PENDING_CANCEL":
                        return OrderStatus.Cancelled;
                    case "REJECTED":
                        return OrderStatus.Rejected;
                    case "EXPIRED":
                        return OrderStatus.Expired;
                    default:
                        return OrderStatus.Pending;
                }
            }

            internal void Update(ApiOrder order)
            {
                this.Status = order.Status;
            }
        }

        class ApiTrade : ITrade
        {
            public ApiTrade(OrderOrTradeUpdatedMessage tr)
            {
                Market = "Binance";
                Symbol = tr.Symbol;
                Date = DateTimeOffset.FromUnixTimeMilliseconds(tr.TradeTime).ToUniversalTime().UtcDateTime;
                Type = tr.Side == "BUY" ? TradeType.Buy : TradeType.Sell;
                Price = (double)tr.LastFilledTradePrice;
                Amount = tr.LastFilledTradeQuantity;
                Fee = Fee;
                FeeAsset = FeeAsset;
                OrderId = tr.OrderId.ToString();
            }
            public decimal Amount { get; private set; }

            public DateTime Date { get; private set; }

            public decimal Fee { get; private set; }

            public string Market { get; private set; }

            public double Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeType Type { get; private set; }

            public string OrderId { get; private set; }

            public string FeeAsset { get; private set; }

            public IOrder Order { get; private set; }
        }
    }
}
