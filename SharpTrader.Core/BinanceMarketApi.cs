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
            SynchOrders(false);
            HearthBeatTimer = new System.Timers.Timer()
            {
                Interval = 5000,
                AutoReset = false,
                Enabled = true,

            };
            HearthBeatTimer.Elapsed += HearthBeat;
        }

        private void SynchOrders(bool raiseEvents = true)
        {
            lock (LockObject)
            {
                List<ApiOrder> newORders = new List<ApiOrder>();
                try
                {
                    _OpenOrders.Clear();
                    Order[] currOrders;
                    lock (Feeds)
                    {
                        currOrders = Feeds.SelectMany(feed => Client.GetCurrentOpenOrders(feed.Symbol).Result).ToArray();
                    }

                    foreach (var order in currOrders)
                    {
                        var to = new ApiOrder(order);
                        var ord = Orders.Where(o => o.Id == to.Id).FirstOrDefault();
                        if (ord != null)
                        {
                            ord.Update(ord);
                            if (ord.Status > OrderStatus.PartiallyFilled)
                            {
                                if (_OpenOrders.Contains(ord))
                                    _OpenOrders.Remove(ord);
                            }
                            else
                            {
                                if (!_OpenOrders.Contains(ord))
                                    _OpenOrders.Add(ord);
                            }
                        }
                        else
                        {
                            Orders.Add(to);
                            if (to.Status <= OrderStatus.PartiallyFilled)
                                _OpenOrders.Add(to);

                            newORders.Add(to);
                        }
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
            if (!SynchOrdersWatchdog.IsRunning || SynchOrdersWatchdog.ElapsedMilliseconds > 30000)
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
                if (order.Status <= OrderStatus.PartiallyFilled)
                    remove = false;//order open
                //update and add
                bool found = false;
                for (int i = 0; i < Orders.Count; i++)
                {
                    if (Orders[i].Id == order.Id)
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
                    if (_OpenOrders[i].Id == msg.OrderId.ToString())
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
                    trade.Order = order;


                    if (msg.ExecutionType == "CANCELLED")
                    {
                        Orders.Remove(order);
                        if (_OpenOrders.Contains(order))
                            _OpenOrders.Remove(order);
                    }
                    order.ResultingTrades.Add(trade);
                }
                else
                    trade.Order = new ApiOrder() { BinanceOrderId = trade.BinanceOrderId };
            }

            OnNewTrade?.Invoke(this, trade);
        }

        public IEnumerable<ITrade> GetTrades(string symbol)
        {
            var binTrades = Client.GetTradeList(symbol, 500, 5000).Result;
            return binTrades.Select(tr => new ApiTrade(symbol, tr));
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

        public decimal GetEquity(string asset)
        {
            throw new NotImplementedException();
        }

        public ISymbolFeed GetSymbolFeed(string symbol)
        {
            lock (Feeds)
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

        }

        public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate)
        {
            NewOrder newOrd;
            lock (LockObject)
            {
                var side = type == TradeType.Buy ? be.OrderSide.BUY : be.OrderSide.SELL;
                newOrd = Client.PostNewOrder(symbol, amount, rate, side, be.OrderType.LIMIT).Result;
            }
            System.Threading.Thread.Sleep(500);
            lock (LockObject)
            {
                var apiOrder = Orders.FirstOrDefault(o => o.Id == newOrd.OrderId.ToString());
                if (apiOrder == null)
                {
                    Console.WriteLine("The order that was just sent was not found");
                    apiOrder = new ApiOrder(newOrd);
                    this._OpenOrders.Add(apiOrder);
                    this.Orders.Add(apiOrder);
                }
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

            var cancel = this.Client.CancelOrder(order.Symbol, int.Parse(order.Id), recvWindow: 5000).Result;

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
                downloader.DownloadCompleteSymbolHistory(Symbol, TimeSpan.FromDays(1));

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
            public string Symbol { get; set; }
            public string Market { get; set; }
            public double Rate { get; set; }
            public decimal Amount { get; set; }
            public string Id => BinanceOrderId.ToString();
            public TradeType TradeType { get; set; }
            public OrderType Type { get; set; }
            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
            internal List<ApiTrade> ResultingTrades { get; set; } = new List<ApiTrade>();
            public IEnumerable<ITrade> Trades => ResultingTrades;
            public int BinanceOrderId { get; set; }
            public decimal Filled { get; set; }

            public ApiOrder() { }

            public ApiOrder(NewOrder binanceOrder)
            {
                Symbol = binanceOrder.Symbol;
                Market = "Binance";
                BinanceOrderId = binanceOrder.OrderId;
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
                Filled = binanceOrder.ExecutedQty;
                BinanceOrderId = binanceOrder.OrderId;
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
                Filled = bo.FilledTradesAccumulatedQuantity;
                BinanceOrderId = bo.OrderId;
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
                this.Filled = order.Filled;
            }
        }

        class ApiTrade : ITrade
        {
            public ApiTrade()
            {

            }
            public ApiTrade(string symbol, Trade tr)
            {

                Market = "Binance";
                Symbol = symbol;
                Date = DateTimeOffset.FromUnixTimeMilliseconds(tr.Time).ToUniversalTime().UtcDateTime;
                Type = tr.IsBuyer ? TradeType.Buy : TradeType.Sell;
                Price = (double)tr.Price;
                Amount = tr.Quantity;
                Fee = Fee;
                FeeAsset = FeeAsset;
                BinanceOrderId = -1;
                BinanceTradeId = tr.Id;
            }
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
                BinanceOrderId = tr.OrderId;
                BinanceTradeId = tr.TradeId;
            }

            public int BinanceTradeId { get; set; }
            public int BinanceOrderId { get; set; }
            public string Id => BinanceTradeId.ToString();
            public string OrderId => BinanceOrderId.ToString();

            public decimal Amount { get; set; }

            public DateTime Date { get; set; }

            public decimal Fee { get; set; }

            public string Market { get; set; }

            public double Price { get; set; }

            public string Symbol { get; set; }

            public TradeType Type { get; set; }



            public string FeeAsset { get; set; }

            public IOrder Order { get; set; }
        }
    }
}
