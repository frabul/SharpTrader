
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SymbolsTable = System.Collections.Generic.Dictionary<string, (string Asset, string Quote)>;


using System.Diagnostics;
using System.Timers;
using Newtonsoft.Json;

using BinanceExchange.API;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using be = BinanceExchange.API;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Websockets;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Models.Response.Error;

namespace SharpTrader
{
    public class BinanceMarketApi : IMarketApi
    {
        public event Action<IMarketApi, ITrade> OnNewTrade;

        private List<ApiOrder> _OpenOrders = new List<ApiOrder>();
        private List<ApiOrder> Orders = new List<ApiOrder>();
        private Stopwatch LastListenTry = new Stopwatch();
        private Stopwatch BalanceUpdateWatchdog = new Stopwatch();
        private Stopwatch SynchOrdersWatchdog = new Stopwatch();
        private Stopwatch TimerUserDataWebSocket = new Stopwatch();
        private HistoricalRateDataBase HistoryDb;
        private BinanceClient Client;
        private DisposableBinanceWebSocketClient WSClient;
        private ExchangeInfoResponse ExchangeInfo;
        //private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();
        private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
        private System.Timers.Timer HearthBeatTimer;
        private SymbolsTable SymbolsTable = new SymbolsTable();

        private List<SymbolFeed> Feeds = new List<SymbolFeed>();
        private object LockObject = new object();
        private Guid UserDataSocket;

        public string MarketName => "Binance";
        public bool Test { get; set; }
        public DateTime Time => DateTime.UtcNow.Add(Client.TimestampOffset);

        public IEnumerable<ISymbolFeed> ActiveFeeds => throw new NotImplementedException();

        public IEnumerable<ITrade> Trades => throw new NotImplementedException();

        public (string Symbol, decimal balance)[] FreeBalances => _Balances.Select(kv => (kv.Key, kv.Value.Free)).ToArray();

        public IEnumerable<IOrder> OpenOrders
        {
            get
            {
                lock (LockObject)
                    return _OpenOrders.ToArray(); // Orders.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled);
            }
        }

        public IEnumerable<string> Symbols => ExchangeInfo.Symbols.Select(sym => sym.Symbol);

        public BinanceMarketApi(string apiKey, string apiSecret, HistoricalRateDataBase historyDb)
        {
            this.HistoryDb = historyDb;

            Client = new BinanceClient(new ClientConfiguration()
            {
                ApiKey = apiKey,
                SecretKey = apiSecret,
            });

            WSClient = new DisposableBinanceWebSocketClient(Client);
            SynchBalance();
            ExchangeInfo = Client.GetExchangeInfo().Result;
            foreach (var symb in ExchangeInfo.Symbols)
            {
                SymbolsTable.Add(symb.Symbol, (symb.BaseAsset, symb.QuoteAsset));
            }
            //download account info
            //todo Synch trades 
            SynchOrders(false);
            ListenUserData();
            HearthBeatTimer = new System.Timers.Timer()
            {
                Interval = 5000,
                AutoReset = false,
                Enabled = true,
            };
            HearthBeatTimer.Elapsed += HearthBeat;
        }

        private void HearthBeat(object state, ElapsedEventArgs elapsed)
        {
            ListenUserData();
            SynchBalance();
            SynchOrders();
            HearthBeatTimer.Start();
        }

        private void SynchOrders(bool raiseEvents = true)
        {
            if (!SynchOrdersWatchdog.IsRunning || SynchOrdersWatchdog.ElapsedMilliseconds > 30000)
            {
                SynchOrdersWatchdog.Restart();
                lock (LockObject)
                {
                    MarketOperation<object> oper = new MarketOperation<object>(MarketOperationStatus.Completed);
                    List<ApiOrder> newORders = new List<ApiOrder>();
                    try
                    {

                        _OpenOrders.Clear();

                        SymbolFeed[] feeds;
                        lock (Feeds)
                        {
                            feeds = Feeds.ToArray();
                        }
                        OrderResponse[] currOrders = feeds
                                .SelectMany(feed =>
                                    Client.GetCurrentOpenOrders(new CurrentOpenOrdersRequest() { Symbol = feed.Symbol }).Result)
                                .ToArray();
                        foreach (var foundOrder in currOrders)
                        {
                            var to = new ApiOrder(foundOrder);
                            var ord = Orders.Where(o => o.Id == to.Id).FirstOrDefault();
                            if (ord != null)
                            {
                                ord.Update(to);
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
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error during orders synchronization: " + GetExceptionErrorInfo(ex));
                    }
                }
            }

        }

        private void ServerTimeSynch()
        {
            try
            {
                var time = Client.GetServerTime().Result;
                var delta = time.ServerTime - DateTime.UtcNow;
                //if (delta > TimeSpan.Zero)
                Client.TimestampOffset = time.ServerTime - DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during server time synch: " + GetExceptionErrorInfo(ex));
            }

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
                        var accountInfo = Client.GetAccountInformation().Result;
                        foreach (var bal in accountInfo.Balances)
                            this._Balances[bal.Asset] = new AssetBalance
                            {
                                Asset = bal.Asset,
                                Free = bal.Free,
                                Locked = bal.Locked
                            };
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error while trying to update account info: " + GetExceptionErrorInfo(ex));
                    }
                }
            }
        }

        private void ListenUserData()
        {
            lock (LockObject)
            {
                if (!TimerUserDataWebSocket.IsRunning || TimerUserDataWebSocket.Elapsed > TimeSpan.FromMinutes(30))
                {
                    TimerUserDataWebSocket.Restart();
                    try
                    {
                        if (UserDataSocket != default(Guid))
                        {
                            WSClient.CloseWebSocketInstance(UserDataSocket);
                            UserDataSocket = default(Guid);
                        }
                    }
                    catch { }

                    try
                    {
                        if (LastListenTry.IsRunning && LastListenTry.ElapsedMilliseconds < 20000)
                            return;

                        UserDataSocket = WSClient.ConnectToUserDataWebSocket(new UserDataWebSocketMessages()
                        {
                            AccountUpdateMessageHandler = HandleAccountUpdatedMessage,
                            OrderUpdateMessageHandler = HandleOrderUpdateMsg,
                            TradeUpdateMessageHandler = HandleTradeUpdateMsg
                        }).Result;

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to listen for user data stream because: " + GetExceptionErrorInfo(ex));
                    }
                    LastListenTry.Restart();
                }
            }

        }

        private void HandleAccountUpdatedMessage(BinanceAccountUpdateData msg)
        {
            lock (LockObject)
            {
                foreach (var bal in msg.Balances)
                {
                    this._Balances[bal.Asset].Free = bal.Free;
                    this._Balances[bal.Asset].Locked = bal.Locked;
                }
            }

        }

        private void HandleOrderUpdateMsg(BinanceTradeOrderData msg)
        {
            lock (LockObject)
            {
                var newOrder = new ApiOrder(msg);
                //update or add
                var order = Orders.FirstOrDefault(o => o.Id == newOrder.Id);
                if (order != null)
                {
                    order.Update(newOrder);
                }
                else
                {
                    Orders.Add(newOrder);
                    order = newOrder;
                }
                var inOpenOrders = _OpenOrders.Contains(order);
                if (inOpenOrders && order.Status > OrderStatus.PartiallyFilled)
                {
                    _OpenOrders.Remove(order);
                }
                else if (!inOpenOrders && order.Status <= OrderStatus.PartiallyFilled)
                {
                    _OpenOrders.Add(order);
                }
            }
        }

        private void HandleTradeUpdateMsg(BinanceTradeOrderData msg)
        {
            var trade = new ApiTrade(msg);
            var ordUpdate = new ApiOrder(msg);
            lock (LockObject)
            {
                var order = Orders.FirstOrDefault(o => o.Id == trade.OrderId);
                if (order != null)
                {
                    order.Update(ordUpdate);
                    trade.Order = order;
                    if (order.Status > OrderStatus.PartiallyFilled)
                    {
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

        public IMarketOperation<IEnumerable<ITrade>> GetLastTrades(string symbol, int count, string fromId)
        {
            try
            {
                long? id = null;
                if (fromId != null)
                    id = long.Parse(fromId);
                var binTrades = Client.GetAccountTrades(new AllTradesRequest { Symbol = symbol, Limit = count, FromId = id }).Result;
                var result = binTrades.Select(tr => new ApiTrade(symbol, tr));
                return new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
            }

        }

        public decimal GetFreeBalance(string asset)
        {
            lock (LockObject)
            {
                if (_Balances.ContainsKey(asset))
                    return _Balances[asset].Free;
            }
            return 0;
        }

        public decimal GetEquity(string asset)
        {
            var allPrices = Client.GetSymbolsPriceTicker().Result;
            lock (LockObject)
            {
                decimal val = 0;
                foreach (var kv in _Balances)
                {
                    if (kv.Key == asset)
                        val += kv.Value.Total;
                    else if (kv.Value.Total != 0)
                    {
                        var sym1 = (kv.Key + asset);
                        var price1 = allPrices.FirstOrDefault(pri => pri.Symbol == sym1);
                        if (price1 != null)
                        {
                            val += ((decimal)price1.Price * kv.Value.Total);
                        }
                        else
                        {
                            var sym2 = asset + kv.Key;
                            var price2 = allPrices.FirstOrDefault(pri => pri.Symbol == sym2);
                            if (price2 != null)
                            {
                                val += (kv.Value.Total / price2.Price);
                            }
                        }
                    }
                }
                return val;
            }
        }

        public ISymbolFeed GetSymbolFeed(string symbol) => GetSymbolFeed(symbol, TimeSpan.FromDays(1000));

        public ISymbolFeed GetSymbolFeed(string symbol, TimeSpan warmup)
        {
            lock (Feeds)
            {
                var feed = Feeds.Where(sf => sf.Symbol == symbol).FirstOrDefault();
                if (feed == null)
                {
                    var (Asset, Quote) = SymbolsTable[symbol];
                    feed = new SymbolFeed(Client, HistoryDb, MarketName, symbol, Asset, Quote, warmup);
                    Feeds.Add(feed);
                }
                return feed;
            }

        }

        public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null)
        {
            ResultCreateOrderResponse newOrd;
            var side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell;
            try
            {
                lock (LockObject)
                {
                    newOrd = (ResultCreateOrderResponse)Client.CreateOrder(
                        new CreateOrderRequest()
                        {
                            Symbol = symbol,
                            Side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell,
                            Quantity = amount / 1.00000000000000000000000000m,
                            NewClientOrderId = clientOrderId,
                            NewOrderResponseType = NewOrderResponseType.Result,
                            Price = rate / 1.00000000000000000000000000000m,
                            Type = be.Enums.OrderType.Limit,
                            TimeInForce = TimeInForce.GTC
                        }).Result;


                }
                System.Threading.Thread.Sleep(250);
                lock (LockObject)
                {
                    var feed = GetSymbolFeed(symbol);
                    var apiOrder = Orders.FirstOrDefault(o => o.Id == newOrd.OrderId.ToString());


                    if (apiOrder == null)
                    {
                        apiOrder = new ApiOrder(newOrd);
                        this._OpenOrders.Add(apiOrder);
                        this.Orders.Add(apiOrder);
                    }
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, apiOrder);
                }
            }
            catch (Exception ex)
            {
                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }


        private static string GetExceptionErrorInfo(Exception ex)
        {
            if (ex is AggregateException ae)
            {
                string msg = "One or more errors: ";
                foreach (var innerExc in ae.InnerExceptions)
                {
                    msg += "\n\t" + GetExceptionErrorInfo(innerExc).Replace("\n", "\n\t");
                    //if (ex is BinanceException binanceExc)
                    //    msg += binanceExc.ErrorDetails.ToString();
                    //else
                    //    msg += "\n\t" + e.Message;
                }
                return msg;
            }
            else if (ex is BinanceException binanceExc)
            {
                return binanceExc.ErrorDetails.ToString();
            }
            else
                return ex.Message;
        }

        public IMarketOperation<IOrder> MarketOrder(string symbol, TradeType type, decimal amount, string clientOrderId = null)
        {
            var side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell;
            try
            {
                var feed = GetSymbolFeed(symbol);
                ApiOrder apiOrder = null;
                lock (LockObject)
                {
                    var ord = (ResultCreateOrderResponse)Client.CreateOrder(
                            new CreateOrderRequest()
                            {
                                Symbol = symbol,
                                Side = side,
                                Type = be.Enums.OrderType.Market,
                                Quantity = (decimal)amount / 1.00000000000000m,
                                NewClientOrderId = clientOrderId,
                                NewOrderResponseType = NewOrderResponseType.Result,   
                            }).Result;
                    apiOrder = new ApiOrder(ord);
                    this._OpenOrders.Add(apiOrder);
                    this.Orders.Add(apiOrder);

                    var quoteBal = _Balances[feed.QuoteAsset];
                    var assetBal = _Balances[feed.Asset];
                    /*
                    if (side == OrderSide.Buy)
                    {
                        quoteBal.Free -= apiOrder.Amount * apiOrder.Price;
                        assetBal.Free +=
                    }
                    else if (side == OrderSide.Sell)
                    {
                        _Balances[feed.Asset] -= apiOrder.Amount;
                    }
                    */
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, apiOrder);
                }
            }
            catch (Exception ex)
            {

                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public IMarketOperation OrderCancel(string id)
        {
            try
            {
                lock (LockObject)
                {
                    var orders = this.Orders.Where(or => or.Id == id);
                    Debug.Assert(orders.Count() < 2, "Two orders with same id");
                    var order = orders.FirstOrDefault();

                    if (order != null)
                    {
                        var cancel = this.Client.CancelOrder(new CancelOrderRequest()
                        {
                            Symbol = order.Symbol,
                            OrderId = order.BinanceOrderId,
                        }).Result;

                        var feed = GetSymbolFeed(order.Symbol);
                        var quoteBal = _Balances[feed.QuoteAsset];
                        var assetBal = _Balances[feed.Asset];

                        if (order.TradeType == TradeType.Buy)
                        {
                            quoteBal.Free += (order.Amount - order.Filled) * (decimal)order.Price;
                        }
                        else if (order.TradeType == TradeType.Sell)
                        {
                            assetBal.Free += order.Amount - order.Filled;
                        }
                        return new MarketOperation<object>(MarketOperationStatus.Completed, order);
                    }
                    else
                        return new MarketOperation<object>($"Unknown order {id}");
                }
            }
            catch (Exception ex)
            {
                return new MarketOperation<object>(GetExceptionErrorInfo(ex));
            }

            //else
            //throw new Exception($"Order {id} not found"); 
        }

        public (decimal min, decimal step) GetMinTradable(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.Symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.LotSize).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterLotSize lotsize)
                    return (lotsize.MinQty, lotsize.StepSize);
            }
            throw new Exception($"Lotsize info not found for symbol {symbol}");
        }

        public decimal GetSymbolPrecision(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.Symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.PriceFilter).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterPrice pf)
                    return pf.TickSize;
            }
            throw new Exception($"Precision info not found for symbol {symbol}");
        }

        public decimal GetMinNotional(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.Symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.Filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.MinNotional).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterMinNotional mn)
                    return mn.MinNotional;
            }
            return 0;
        }

        public IMarketOperation<IOrder> QueryOrder(string symbol, string id)
        {
            try
            {
                var binOrd = Client.QueryOrder(new QueryOrderRequest() { OrderId = long.Parse(id), Symbol = symbol }).Result;
                var ord = new ApiOrder(binOrd);
                lock (LockObject)
                {
                    var oldOrder = this.Orders.FirstOrDefault(o => o.Id == ord.Id);
                    if (oldOrder != null)
                    {
                        oldOrder.Update(ord);
                        ord = oldOrder;
                    }
                    else
                    {
                        Orders.Add(ord);
                        if (ord.Status < OrderStatus.PartiallyFilled)
                            _OpenOrders.Add(ord);
                    }
                }
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, ord);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }

        }

        class MarketOperation<T> : IMarketOperation<T>
        {



            public T Result { get; }
            public string ErrorInfo { get; internal set; }
            public MarketOperationStatus Status { get; internal set; }

            public MarketOperation(MarketOperationStatus status)
            {
                Status = status;

            }
            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }

            public MarketOperation(string errorInfo)
            {
                this.Status = MarketOperationStatus.Failed;
                ErrorInfo = errorInfo;
            }
        }

        class SymbolFeed : SymbolFeedBoilerplate, ISymbolFeed
        {
            private BinanceClient Client;
            private DisposableBinanceWebSocketClient WebSocketClient;
            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            private HistoricalRateDataBase HistoryDb;
            private object Locker = new object();
            private Guid KlineSocket;
            private Guid PartialDepthSocket;

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

            public SymbolFeed(BinanceClient client, HistoricalRateDataBase hist, string market, string symbol, string asset, string quoteAsset, TimeSpan historyToLoad)
            {
                HistoryDb = hist;
                this.Client = client;
                this.WebSocketClient = new DisposableBinanceWebSocketClient(Client);
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
                //load missing data to hist db 
                Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");

                //--- download latest data
                var loadStart = TimeSpan.FromHours(6) < historyToLoad ? TimeSpan.FromHours(6) : historyToLoad;
                var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb, Client);
                downloader.DownloadHistoryAsync(Symbol, historyToLoad, loadStart);

                //--- load the history into this 
                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(this.Market, Symbol, TimeSpan.FromSeconds(60));
                symbolHistory.Ticks.SeekNearestAfter(DateTime.UtcNow - historyToLoad);
                while (symbolHistory.Ticks.Next())
                    this.Ticks.AddRecord(symbolHistory.Ticks.Tick);

                this.Ask = this.Bid = Ticks.LastTick.Close;

                HearthBeatTimer = new Timer(1000)
                {
                    AutoReset = false,
                    Enabled = true,
                };
                HearthBeatTimer.Elapsed += HearthBeat;
            }


            void HearthBeat(object state, ElapsedEventArgs args)
            {
                if (!KlineWatchdog.IsRunning || KlineWatchdog.ElapsedMilliseconds > 70000)
                {
                    KlineListen();
                }
                if (!PartialDepthWatchDog.IsRunning || PartialDepthWatchDog.ElapsedMilliseconds > 50000)
                {

                    PartialDepthListen();
                }
                HearthBeatTimer.Start();
            }

            void KlineListen()
            {
                try
                {
                    if (KlineSocket != default(Guid))
                        WebSocketClient.CloseWebSocketInstance(KlineSocket);
                    KlineSocket = WebSocketClient.ConnectToKlineWebSocket(this.Symbol.ToLower(), KlineInterval.OneMinute, HandleKlineEvent);
                    KlineWatchdog.Restart();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception during KlineListen: " + GetExceptionErrorInfo(ex));
                }
            }

            void PartialDepthListen()
            {
                try
                {
                    if (PartialDepthSocket != default(Guid))
                        WebSocketClient.CloseWebSocketInstance(PartialDepthSocket);
                    PartialDepthSocket = WebSocketClient.ConnectToPartialDepthWebSocket(this.Symbol.ToLower(), PartialDepthLevels.Five, HandlePartialDepthUpdate);
                    PartialDepthWatchDog.Restart();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception during PartialDepthListen: " + GetExceptionErrorInfo(ex));
                }

            }

            private void HandlePartialDepthUpdate(BinancePartialData messageData)
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

            private void HandleKlineEvent(BinanceKlineData msg)
            {
                KlineWatchdog.Restart();
                this.Bid = (double)msg.Kline.Close;

                if (msg.Kline.IsBarFinal)
                {

                    var candle = new Candlestick()
                    {
                        Close = (double)msg.Kline.Close,
                        High = (double)msg.Kline.High,
                        CloseTime = msg.Kline.EndTime.AddMilliseconds(1),
                        OpenTime = msg.Kline.EndTime,
                        Low = (double)msg.Kline.Low,
                        Open = (double)msg.Kline.Open,
                        Volume = (double)msg.Kline.Volume
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
            public double Price { get; set; }
            public decimal Amount { get; set; }
            public string Id => BinanceOrderId.ToString();
            public string ClientId { get; set; }
            public TradeType TradeType { get; set; }
            public OrderType Type { get; set; }
            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
            internal List<ApiTrade> ResultingTrades { get; set; } = new List<ApiTrade>();
            public IEnumerable<ITrade> Trades => ResultingTrades;
            public long BinanceOrderId { get; set; }
            public decimal Filled { get; set; }

            public ApiOrder() { }

            public ApiOrder(AcknowledgeCreateOrderResponse binanceOrder)
            {
                BinanceOrderId = binanceOrder.OrderId;
                ClientId = binanceOrder.ClientOrderId;
                Symbol = binanceOrder.Symbol;
                Market = "Binance";

            }
            public ApiOrder(ResultCreateOrderResponse binanceOrder)
            {
                BinanceOrderId = binanceOrder.OrderId;
                ClientId = binanceOrder.ClientOrderId;
                Symbol = binanceOrder.Symbol;
                Market = "Binance";

                TradeType = binanceOrder.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(binanceOrder.Type);
                Amount = binanceOrder.OriginalQuantity;
                Price = (double)binanceOrder.Price;
                Status = GetStatus(binanceOrder.Status);
                Filled = binanceOrder.ExecutedQuantity;
            }

            public ApiOrder(OrderResponse or)
            {
                Symbol = or.Symbol;
                Market = "Binance";
                TradeType = or.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(or.Type);
                Amount = or.OriginalQuantity;
                Price = (double)or.Price;
                Status = GetStatus(or.Status);
                Filled = or.ExecutedQuantity;
                BinanceOrderId = or.OrderId;


            }



            public ApiOrder(BinanceTradeOrderData bo)
            {
                BinanceOrderId = bo.OrderId;
                Symbol = bo.Symbol;
                Market = "Binance";
                TradeType = bo.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(bo.Type);
                Amount = bo.Quantity;
                Price = (double)bo.Price;
                Status = GetStatus(bo.OrderStatus);
                Filled = bo.AccumulatedQuantityOfFilledTradesThisOrder;
            }

            private static OrderType GetOrderType(be.Enums.OrderType type)
            {
                switch (type)
                {
                    case be.Enums.OrderType.Limit:
                        return OrderType.Limit;
                    case be.Enums.OrderType.Market:
                        return OrderType.Market;
                    case be.Enums.OrderType.StopLoss:
                        return OrderType.StopLoss;
                    case be.Enums.OrderType.StopLossLimit:
                        return OrderType.StopLossLimit;
                    case be.Enums.OrderType.TakeProfit:
                        return OrderType.TakeProfit;
                    case be.Enums.OrderType.TakeProfitLimit:
                        return OrderType.TakeProfitLimit;
                    case be.Enums.OrderType.LimitMaker:
                        return OrderType.LimitMaker;
                    default:
                        return OrderType.Unknown;
                }
            }
            private static OrderStatus GetStatus(be.Enums.OrderStatus status)
            {
                switch (status)
                {
                    case be.Enums.OrderStatus.New:
                        return OrderStatus.Pending;
                    case be.Enums.OrderStatus.PartiallyFilled:
                        return OrderStatus.PartiallyFilled;
                    case be.Enums.OrderStatus.Filled:
                        return OrderStatus.Filled;
                    case be.Enums.OrderStatus.Cancelled:
                        return OrderStatus.Cancelled;
                    case be.Enums.OrderStatus.PendingCancel:
                        return OrderStatus.PendingCancel;
                    case be.Enums.OrderStatus.Rejected:
                        return OrderStatus.Rejected;
                    case be.Enums.OrderStatus.Expired:
                        return OrderStatus.Expired;
                    default:
                        throw new Exception("Unknown order status");
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
            public ApiTrade(string symbol, AccountTradeReponse tr)
            {

                Market = "Binance";
                Symbol = symbol;
                Type = tr.IsBuyer ? TradeType.Buy : TradeType.Sell;
                Price = (double)tr.Price;
                Amount = tr.Quantity;
                Fee = tr.Commission;
                FeeAsset = tr.CommissionAsset;
                Time = tr.Time;
                BinanceOrderId = -1;
                BinanceTradeId = tr.Id;
            }

            public ApiTrade(BinanceTradeOrderData tr)
            {
                Market = "Binance";
                Symbol = tr.Symbol;
                Type = tr.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Price = (double)tr.PriceOfLastFilledTrade;
                Amount = tr.QuantityOfLastFilledTrade;
                Fee = Fee;
                FeeAsset = FeeAsset;
                Time = tr.TimeStamp;
                BinanceOrderId = tr.OrderId;
                BinanceTradeId = tr.TradeId;
            }

            public long BinanceTradeId { get; set; }
            public long BinanceOrderId { get; set; }
            public string Id => BinanceTradeId.ToString();
            public string OrderId => BinanceOrderId.ToString();

            public decimal Amount { get; set; }

            public decimal Fee { get; set; }

            public string Market { get; set; }

            public double Price { get; set; }

            public string Symbol { get; set; }

            public TradeType Type { get; set; }

            public string FeeAsset { get; set; }

            public IOrder Order { get; set; }

            public DateTime Time { get; set; }
        }

        class AssetBalance
        {
            public string Asset;
            public decimal Free;
            public decimal Locked;
            public decimal Total => Free + Locked;
        }

    }
}
