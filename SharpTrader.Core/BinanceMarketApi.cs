
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
        private HistoricalRateDataBase HistoryDb = new HistoricalRateDataBase(".\\Data\\");
        private BinanceClient Client;
        private DisposableBinanceWebSocketClient WSClient;
        private ExchangeInfoResponse ExchangeInfo;
        private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();

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
                    List<ApiOrder> newORders = new List<ApiOrder>();
                    try
                    {
                        _OpenOrders.Clear();
                        OrderResponse[] currOrders;
                        lock (Feeds)
                        {
                            currOrders = Feeds
                                .SelectMany(feed =>
                                    Client.GetCurrentOpenOrders(new CurrentOpenOrdersRequest() { Symbol = feed.Symbol }).Result)
                                .ToArray();
                        }

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
                        Console.WriteLine("Error while synch orders: " + ex.Message);
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
            catch
            {
                Console.WriteLine("Error during server time synch");
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
                            this._Balances[bal.Asset] = bal.Free;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error while trying to update account info " + ex.InnerException?.Message);
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
                    catch
                    {
                        Console.WriteLine("Failed to listen for user data stream.");
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
                    this._Balances[bal.Asset] = bal.Free;
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

        public IEnumerable<ITrade> GetLastTrades(string symbol, int count, string fromId)
        {
            long? id = null;
            if (fromId != null)
                id = long.Parse(fromId); 
            var binTrades = Client.GetAccountTrades(new AllTradesRequest { Symbol = symbol, Limit = count, FromId = id }).Result;
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

        public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null)
        {
            ResultCreateOrderResponse newOrd;
            var side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell;
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
                                Quantity = (decimal)amount / 1.00000000000000m,
                                NewClientOrderId = clientOrderId,
                                NewOrderResponseType = NewOrderResponseType.Result,
                                TimeInForce = TimeInForce.GTC,
                                Price = (decimal)(side == OrderSide.Buy ? feed.Ask : feed.Bid)

                            }).Result;
                    apiOrder = new ApiOrder(ord);
                    this._OpenOrders.Add(apiOrder);
                    this.Orders.Add(apiOrder);

                    if (side == OrderSide.Buy)
                        _Balances[feed.QuoteAsset] -= apiOrder.Amount;
                    else if (side == OrderSide.Sell)
                        _Balances[feed.Asset] -= apiOrder.Amount;

                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, apiOrder);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Market operation failed because: " + ex.InnerException.Message);
                return new MarketOperation<IOrder>(MarketOperationStatus.Failed, null);
            }
        }

        public void OrderCancel(string id)
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
                    if (order.TradeType == TradeType.Buy)
                        _Balances[feed.QuoteAsset] += order.Amount - order.Filled;
                    else if (order.TradeType == TradeType.Sell)
                        _Balances[feed.Asset] += order.Amount - order.Filled;
                }
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

        public IOrder QueryOrder(string symbol, string id)
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

            return ord;
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

            public SymbolFeed(BinanceClient client, HistoricalRateDataBase hist, string market, string symbol, string asset, string quoteAsset)
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

                var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb, Client);
                downloader.DownloadCompleteSymbolHistory(Symbol, TimeSpan.FromDays(1));

                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(this.Market, Symbol, TimeSpan.FromSeconds(60));

                while (symbolHistory.Ticks.Next())
                    this.Ticks.AddRecord(symbolHistory.Ticks.Tick);

                PartialDepthListen();
                KlineListen();
                HearthBeatTimer = new Timer(5000)
                {
                    AutoReset = false,
                    Enabled = true,
                };
                HearthBeatTimer.Elapsed += HearthBeat;
            }


            void HearthBeat(object state, ElapsedEventArgs args)
            {
                if (KlineWatchdog.ElapsedMilliseconds > 70000)
                {
                    KlineListen();
                }
                if (PartialDepthWatchDog.ElapsedMilliseconds > 40000)
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
                }
                catch { }

                KlineSocket = WebSocketClient.ConnectToKlineWebSocket(this.Symbol.ToLower(), KlineInterval.OneMinute, HandleKlineEvent);
                KlineWatchdog.Restart();
            }
            void PartialDepthListen()
            {
                try
                {
                    if (PartialDepthSocket != default(Guid))
                        WebSocketClient.CloseWebSocketInstance(PartialDepthSocket);
                }
                catch { }
                PartialDepthSocket = WebSocketClient.ConnectToPartialDepthWebSocket(this.Symbol.ToLower(), 5, HandlePartialDepthUpdate);
                PartialDepthWatchDog.Restart();
            }
            private void HandleDepthUpdate(BinanceDepthData messageData)
            {
                PartialDepthWatchDog.Restart();
                var bid = (double)messageData.BidDepthDeltas.Where(level => level.Quantity != 0).Max(l => l.Quantity);
                var ask = (double)messageData.AskDepthDeltas.Where(level => level.Quantity != 0).Min(l => l.Quantity);
                if (bid != 0 && ask != 0)
                {
                    this.Bid = bid;
                    this.Ask = ask;
                    Spread = Ask - Bid;
                    SignalTick();
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
    }
}
