
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.Response.Error;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using be = BinanceExchange.API;

namespace SharpTrader
{
    public class QuoteTick : IBaseData
    {
        public double Bid { get; }
        public double Ask { get; }
        public DateTime Time { get; }

        public double Value => Bid;

        public MarketDataKind Kind => MarketDataKind.QuoteTick;

        public double Low => Bid;

        public double High => Bid;

        public QuoteTick(double bid, double ask, DateTime eventTime)
        {
            this.Bid = bid;
            this.Ask = ask;
            this.Time = eventTime;
        }
    }

    public class BinanceMarketApi : IMarketApi
    {
        public event Action<IMarketApi, ITrade> OnNewTrade;

        private readonly object LockOrdersTrades = new object();
        private readonly object LockBalances = new object();
        private readonly List<ApiOrder> OrdersActive = new List<ApiOrder>();
        private readonly Stopwatch StopwatchSocketClose = new Stopwatch();
        private readonly Stopwatch UserDataPingStopwatch = new Stopwatch();
        private readonly Dictionary<string, long> UpdatedTrades = new Dictionary<string, long>();

        private LiteCollection<ApiOrder> Orders;
        private LiteCollection<ApiTrade> Trades;
        private LiteCollection<ApiOrder> OrdersArchive;
        private LiteCollection<ApiTrade> TradesArchive;
        private HistoricalRateDataBase HistoryDb;
        private BinanceWebSocketClient WSClient;
        private CombinedWebSocketClient CombinedWebSocketClient;
        private ExchangeInfoResponse ExchangeInfo;
        private NLog.Logger Logger;
        private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
        private System.Timers.Timer TimerListenUserData;
        private LiteDatabase TradesAndOrdersArch;
        private LiteDatabase TradesAndOrdersDb;
        private List<SymbolFeed> Feeds = new List<SymbolFeed>();
        private Guid UserDataSocket;
        private Regex IdRegex = new Regex("([A-Z]+)([0-9]+)", RegexOptions.Compiled);
        private System.Timers.Timer TimerFastUpdates;
        private System.Timers.Timer TimerOrdersTradesSynch;
        private DateTime LastOperationsArchivingTime = DateTime.MinValue;
        private string OperationsDbPath;
        private string OperationsArchivePath;

        public BinanceClient Client { get; private set; }

        public string MarketName => "Binance";

        public DateTime Time => DateTime.UtcNow.Add(Client.TimestampOffset);

        IEnumerable<ITrade> IMarketApi.Trades => Trades.FindAll().ToArray();

        IEnumerable<IOrder> IMarketApi.OpenOrders { get { lock (LockOrdersTrades) return OrdersActive.ToArray(); } }

        public (string Symbol, decimal balance)[] FreeBalances
        {
            get
            {
                lock (LockBalances)
                    return _Balances.Select(kv => (kv.Key, kv.Value.Free)).ToArray();
            }
        }

        public BinanceMarketApi(string apiKey, string apiSecret, HistoricalRateDataBase historyDb, bool resynchTradesAndOrders = false, double rateLimitFactor = 1)
        {
            Logger = LogManager.GetLogger("BinanceMarketApi");
            Logger.Info("starting initialization...");
            this.HistoryDb = historyDb;
            OperationsDbPath = Path.Combine("Data", "BinanceAccountsData", $"{apiKey}_tnd.db");
            OperationsArchivePath = Path.Combine("Data", "BinanceAccountsData", $"{apiKey}_tnd_archive.db");
            InitializeOperationsDb();

            Client = new BinanceClient(new ClientConfiguration()
            {
                ApiKey = apiKey ?? "null",
                SecretKey = apiSecret ?? "null",
                RateLimitFactor = rateLimitFactor
            });

            ExchangeInfo = Client.GetExchangeInfo().Result;
            WSClient = new BinanceWebSocketClient(Client);
            CombinedWebSocketClient = new CombinedWebSocketClient();
            this.ServerTimeSynch().Wait();
            Logger.Info("Archiving old oders...");
            ArchiveOldOperations();


            if (resynchTradesAndOrders)
                SynchAllOperations().Wait();
            else
            {
                Logger.Info("Synching oders and trades....");
                if (apiKey != null)
                {
                    SynchOpenOrders().Wait();
                    SynchLastTrades().Wait();
                }
            }

            if (apiKey != null)
            {
                Task.WaitAll(ListenUserData(), SynchBalance());
                TimerListenUserData = new System.Timers.Timer(30000)
                {
                    AutoReset = false,
                    Enabled = true,
                };
                TimerListenUserData.Elapsed += (s, ea) => ListenUserData().ContinueWith(t => TimerListenUserData.Start());
                //----------
                TimerOrdersTradesSynch = new System.Timers.Timer(60000)
                {
                    AutoReset = false,
                    Enabled = true,
                };
                TimerOrdersTradesSynch.Elapsed +=
                    async (s, e) =>
                    {
                        //we first synch open orders 
                        await SynchOpenOrders();
                        //then last trades
                        await SynchLastTrades();
                        //then finally we restart the timer and archive operations 
                        TimerOrdersTradesSynch.Start();
                        ArchiveOldOperations();
                    };
            }

            TimerFastUpdates = new System.Timers.Timer(15000)
            {
                AutoReset = false,
                Enabled = true,
            };
            TimerFastUpdates.Elapsed +=
                async (s, e) =>
                {
                    //first synch server time then balance then restart timer
                    if (apiKey != null)
                    {
                        await ServerTimeSynch();
                        await SynchBalance();
                        TimerFastUpdates.Start();
                    }
                    else
                    {
                        await ServerTimeSynch();
                        TimerFastUpdates.Start();
                    }
                };

            Logger.Info("initialization complete");
        }

        private void ArchiveOldOperations()
        {

            if (LastOperationsArchivingTime + TimeSpan.FromHours(24) < DateTime.Now)
                lock (LockOrdersTrades)
                {
                    TimeToArchive = Time - TimeSpan.FromDays(30);
                    List<ApiOrder> ordersToMove = Orders.Find(o => o.Time < TimeToArchive).ToList();
                    List<ApiTrade> tradesToMove = new List<ApiTrade>();
                    foreach (var order in ordersToMove)
                    {
                        tradesToMove.AddRange(Trades.Find(t => t.OrderId == order.OrderId && t.Symbol == order.Symbol).ToArray());
                    }
                    foreach (var trade in tradesToMove.ToArray())
                    {
                        Trades.Delete(tr => tr.Id == trade.Id);
                        if (TradesArchive.FindById(trade.Id) != null)
                            tradesToMove.Remove(trade);

                    }

                    foreach (var ord in ordersToMove.ToArray())
                    {
                        Orders.Delete(o => o.Id == ord.Id);
                        if (OrdersArchive.FindById(ord.Id) != null)
                            ordersToMove.Remove(ord);
                    }


                    TradesArchive.InsertBulk(tradesToMove);
                    OrdersArchive.InsertBulk(ordersToMove);

                    TradesAndOrdersDb.Shrink();
                    LastOperationsArchivingTime = DateTime.Now;
                    TradesAndOrdersDb.Dispose();
                    TradesAndOrdersArch.Dispose();
                    InitializeOperationsDb();
                }
        }

        private DateTime TimeToArchive;

        private void InitializeOperationsDb()
        {

            if (!Directory.Exists(Path.GetDirectoryName(OperationsDbPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(OperationsDbPath));

            //----
            TradesAndOrdersArch = new LiteDatabase(OperationsArchivePath);
            TradesAndOrdersDb = new LiteDatabase(OperationsDbPath);

            var dbs = new[] { TradesAndOrdersDb, TradesAndOrdersArch };
            foreach (var db in dbs)
            {
                var orders = db.GetCollection<ApiOrder>("Orders");
                orders.EnsureIndex(o => o.Id, true);
                orders.EnsureIndex(o => o.OrderId);
                orders.EnsureIndex(o => o.Symbol);
                orders.EnsureIndex(o => o.Filled);
                orders.EnsureIndex(o => o.Status);

                var trades = db.GetCollection<ApiTrade>("Trades");
                trades.EnsureIndex(o => o.Id, true);
                trades.EnsureIndex(o => o.TradeId);
                trades.EnsureIndex(o => o.Symbol);
                trades.EnsureIndex(o => o.OrderId);
                trades.EnsureIndex(o => o.TradeId);
            }
            //----
            Orders = TradesAndOrdersDb.GetCollection<ApiOrder>("Orders");
            Trades = TradesAndOrdersDb.GetCollection<ApiTrade>("Trades");
            OrdersArchive = TradesAndOrdersArch.GetCollection<ApiOrder>("Orders");
            TradesArchive = TradesAndOrdersArch.GetCollection<ApiTrade>("Trades");
        }

        private async Task SynchAllOperations()
        {
            var symbols = ExchangeInfo.Symbols.Select(s => s.symbol).ToArray();
            List<Task> tasks = new List<Task>();
            int cnt = 0;
            foreach (var sym in symbols)
            {
                cnt++;
                while (tasks.Where(t => !t.IsCompleted).Count() > 5)
                    await Task.Delay(100);
                var task = SynchSymbolOrders(sym);
                if (cnt % 10 == 0)
                    task = task.ContinueWith(t => Logger.Info($"Synching orders: {cnt}/{symbols.Length}"));
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
            tasks.Clear();
            foreach (var sym in symbols)
            {
                var orders = Orders.Find(o => o.Symbol == sym);
                if (orders != null && orders.Count() > 0)
                {
                    while (tasks.Where(t => !t.IsCompleted).Count() > 5)
                        await Task.Delay(100);
                    tasks.Add(SynchAllSymbolTrades(sym));
                }
            }
            await Task.WhenAll(tasks);
        }

        private async Task SynchSymbolOrders(string sym)
        {

            var lastOrder = Orders.Find(o => o.Symbol == sym)?.OrderBy(o => o.OrderId).LastOrDefault();
            long start = (lastOrder != null) ? lastOrder.OrderId + 1 : 0;
            bool finish = false;
            List<ApiOrder> orders = new List<ApiOrder>();
            while (!finish)
            {
                var responses = await Client.GetAllOrders(new AllOrdersRequest() { OrderId = start, Symbol = sym });
                var toInsert = responses.Select(or => new ApiOrder(or)).OrderBy(o => o.OrderId).ToArray();
                orders.AddRange(toInsert);
                if (toInsert.Length > 0)
                    start = toInsert.LastOrDefault().OrderId + 1;
                else finish = true;
            }
            foreach (var ord in orders)
                Orders.Insert(ord);
            //if (orders.Count > 1)
            //    OrdersC.InsertBulk(orders );
        }

        private async Task SynchAllSymbolTrades(string sym)
        {
            var lastTrade = Trades.Find(o => o.Symbol == sym)?.OrderBy(o => o.TradeId).LastOrDefault();
            long start = (lastTrade != null) ? lastTrade.TradeId + 1 : 0;
            bool finish = false;
            List<ApiTrade> trades = new List<ApiTrade>();
            while (!finish)
            {
                var responses = await Client.GetAccountTrades(new AllTradesRequest() { FromId = start, Symbol = sym });
                var toInsert = responses.Select(or => new ApiTrade(sym, or)).OrderBy(o => o.TradeId).ToArray();
                foreach (var tr in toInsert)
                {
                    var order = Orders.FindOne(o => o.OrderId == tr.OrderId);
                    if (order == null)
                        order = OrderSynchAsync(tr.Symbol + tr.OrderId).Result.Result as ApiOrder;
                    tr.ClientOrderId = order.ClientId;
                }

                trades.AddRange(toInsert);
                if (toInsert.Length > 0)
                    start = toInsert.LastOrDefault().TradeId + 1;
                else
                    finish = true;
            }
            if (trades.Count > 1)
                Trades.InsertBulk(trades);
        }

        private async Task ServerTimeSynch()
        {
            try
            {
                var time = await Client.GetServerTime();
                var delta = time.ServerTime - DateTime.UtcNow;
                Client.TimestampOffset = time.ServerTime - DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during server time synch: " + GetExceptionErrorInfo(ex));
            }

        }

        private async Task SynchBalance()
        {
            try
            {
                var accountInfo = await Client.GetAccountInformation();
                foreach (var bal in accountInfo.Balances)
                {
                    this._Balances[bal.Asset] = new AssetBalance
                    {
                        Asset = bal.Asset,
                        Free = bal.Free,
                        Locked = bal.Locked
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while trying to update account info: " + GetExceptionErrorInfo(ex));
            }


        }

        /// <summary>
        /// Synchronizes all open orders
        /// </summary> 
        private async Task SynchOpenOrders()
        {
            MarketOperation<object> oper = new MarketOperation<object>(MarketOperationStatus.Completed);
            try
            {
                var resp = await Client.GetCurrentOpenOrders(new CurrentOpenOrdersRequest() { Symbol = null });
                var allOpen = resp.Select(o => new ApiOrder(o));
                ApiOrder[] toClose;
                lock (LockOrdersTrades)
                {
                    foreach (var newOrder in allOpen)
                    {
                        OrdersUpdateOrInsert(newOrder);

                        var foundOrder = OrdersActive.FirstOrDefault(o => o.Id == newOrder.Id);
                        if (foundOrder != null)
                            foundOrder.Update(newOrder);
                        else
                            OrdersActive.Add(newOrder);
                    }


                    toClose = OrdersActive.Where(oo => !allOpen.Any(no => no.Id == oo.Id)).ToArray();
                    foreach (var orderToClose in toClose)
                        OrdersActive.Remove(orderToClose);
                }

                foreach (var ord in toClose)
                {
                    if (ord.Status <= OrderStatus.PartiallyFilled)
                    {
                        var orderUpdated =
                            new ApiOrder(await Client.QueryOrder(new QueryOrderRequest() { OrderId = ord.OrderId, Symbol = ord.Symbol }));
                        ord.Update(orderUpdated);
                        OrdersUpdateOrInsert(orderUpdated);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during orders synchronization: " + GetExceptionErrorInfo(ex));
            }
        }

        private async Task SynchLastTrades()
        {
            foreach (var sym in ExchangeInfo.Symbols.Select(s => s.symbol))
            {
                await SynchLastTrades(sym);
            }
        }

        public async Task SynchLastTrades(string sym, bool force = false)
        {
            if (sym == null)
                throw new Exception("Parameter sym cannot be null");
            try
            {
                bool hasActiveOrders = false;
                lock (LockOrdersTrades)
                    hasActiveOrders = OrdersActive.Any(o => o.Symbol == sym);

                var sOrders = Orders.Find(o => o.Symbol == sym).OrderBy(o => o.OrderId);
                var lastOrder = sOrders.LastOrDefault();

                if (lastOrder != null)
                {
                    var updatedAlready = UpdatedTrades.TryGetValue(sym, out long lastOrderUpdate);
                    if (!updatedAlready || lastOrderUpdate != lastOrder.OrderId || hasActiveOrders || force)
                    {
                        var resp = await Client.GetAccountTrades(new AllTradesRequest { Symbol = sym, Limit = 100 });
                        var trades = resp.Select(tr => new ApiTrade(sym, tr));
                        foreach (var tr in trades)
                        {
                            ApiOrder order;
                            lock (LockOrdersTrades)
                            {
                                order = Orders.FindOne(o => o.OrderId == tr.OrderId && o.Symbol == tr.Symbol);
                                if (order == null)
                                    order = OrdersArchive.FindOne(o => o.OrderId == tr.OrderId && o.Symbol == tr.Symbol);
                            }
                            // put out of lokc to prevent deadlock
                            if (order == null)
                                order = OrderSynchAsync(tr.Symbol + tr.OrderId).Result.Result as ApiOrder;
                            tr.ClientOrderId = order.ClientId;

                            lock (LockOrdersTrades)
                                TradesUpdateOrInsert(tr);
                        }
                        //if has active orders we want it to continue updating when the active order becomes inactive
                        if (!hasActiveOrders)
                            UpdatedTrades[sym] = lastOrder.OrderId;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error during {sym} trades synch: {GetExceptionErrorInfo(ex)}");
            }
        }

        private async Task ListenUserData()
        {
            try
            {
                if (UserDataSocket != default && (!UserDataPingStopwatch.IsRunning || UserDataPingStopwatch.Elapsed > TimeSpan.FromMinutes(1)))
                {
                    UserDataPingStopwatch.Restart();
                    try
                    {
                        if (!WSClient.PingWebSocketInstance(UserDataSocket))
                        {
                            CloseUserDataSocket();
                            Logger.Info("Closing user data socket because ping returned false.");
                        }
                    }
                    catch { UserDataSocket = default; }
                }

                //every 120 minutes close the socket
                if (!StopwatchSocketClose.IsRunning || StopwatchSocketClose.Elapsed > TimeSpan.FromMinutes(120))
                {
                    StopwatchSocketClose.Restart();
                    CloseUserDataSocket();
                }


                if (UserDataSocket == default)
                {
                    UserDataSocket = await WSClient.ConnectToUserDataWebSocket(new UserDataWebSocketMessages()
                    {
                        AccountUpdateMessageHandler = HandleAccountUpdatedMessage,
                        OrderUpdateMessageHandler = HandleOrderUpdateMsg,
                        TradeUpdateMessageHandler = HandleTradeUpdateMsg
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to listen for user data stream because: " + GetExceptionErrorInfo(ex));
            }

        }

        private void CloseUserDataSocket()
        {
            try
            {
                if (UserDataSocket != default)
                {
                    WSClient.CloseWebSocketInstance(UserDataSocket);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed closing user data stream because: " + GetExceptionErrorInfo(ex));
            }
            UserDataSocket = default;
        }

        private void HandleAccountUpdatedMessage(BinanceAccountUpdateData msg)
        {
            lock (LockBalances)
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
            var newOrder = new ApiOrder(msg);
            //update or add in database 
            OrdersUpdateOrInsert(newOrder);
            OrdersActiveInsertOrUpdate(newOrder);

        }

        private void HandleTradeUpdateMsg(BinanceTradeOrderData msg)
        {
            var tradeUpdate = new ApiTrade(msg);
            var ordUpdate = new ApiOrder(msg);

            var order = Orders.FindOne(o => o.Id == ordUpdate.Id);
            if (order != null)
                order.Update(ordUpdate);
            else
                order = ordUpdate;

            if (!order.ResultingTrades.Contains(tradeUpdate.TradeId))
                order.ResultingTrades.Add(tradeUpdate.TradeId);
            lock (LockOrdersTrades)
            {
                OrdersUpdateOrInsert(order);
                //-----
                OrdersActiveInsertOrUpdate(order);
                TradesUpdateOrInsert(tradeUpdate);
            }
        }

        private void OrdersUpdateOrInsert(ApiOrder newOrder)
        {
            lock (LockOrdersTrades)
            {
                if (newOrder.Time < TimeToArchive)
                {
                    //the order is archived or we should archive it
                    OrdersArchive.Upsert(newOrder);
                }
                else
                {
                    var ord = Orders.FindOne(o => o.Id == newOrder.Id);
                    if (ord != null)
                    {
                        ord.Update(newOrder);
                        Orders.Update(newOrder);
                    }
                    else
                    {
                        Orders.Insert(newOrder);
                    }
                }
            }
        }

        private void OrdersActiveInsertOrUpdate(ApiOrder newOrder)
        {
            ApiOrder oldOpen;
            lock (LockOrdersTrades)
            {
                oldOpen = OrdersActive.FirstOrDefault(oo => oo.Id == newOrder.Id);
                if (oldOpen != null)
                {
                    oldOpen.Update(newOrder);
                    if (oldOpen.Status > OrderStatus.PartiallyFilled)
                        OrdersActive.Remove(oldOpen);
                }
                else if (newOrder.Status <= OrderStatus.PartiallyFilled)
                {
                    OrdersActive.Add(newOrder);
                }
            }
        }

        private void TradesUpdateOrInsert(ApiTrade newTrade)
        {
            lock (LockOrdersTrades)
            {
                var tradeInDb = Trades.FindOne(t => t.Id == newTrade.Id);
                if (newTrade.ClientOrderId == null || newTrade.ClientOrderId == "null")
                {
                    var order = Orders.FindOne(o => o.OrderId == newTrade.OrderId && o.Symbol == newTrade.Symbol);
                    newTrade.ClientOrderId = order?.ClientId;
                }

                if (tradeInDb != null)
                    Trades.Update(newTrade);
                else if (TradesArchive.FindOne(tr => tr.Id == newTrade.Id) != null)
                {

                }
                else
                {
                    Trades.Insert(newTrade);
                    OnNewTrade?.Invoke(this, newTrade);
                    Logger.Trace($"New trade {newTrade.Id}");
                }
            }

        }

        private (string symbol, long id) DeconstructId(string idString)
        {
            var match = IdRegex.Match(idString);
            if (!match.Success)
                throw new ArgumentException("Bad id");
            var symbol = match.Groups[1].Value;
            var id = int.Parse(match.Groups[2].Value);
            return (symbol, id);
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

        public Task<IMarketOperation<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId)
        {
            try
            {
                int tradeId = 0;
                if (fromId != null)
                {
                    var match = IdRegex.Match(fromId);
                    if (!match.Success)
                        throw new ArgumentException("Bad id");

                    if (symbol != match.Groups[1].Value)
                        throw new ArgumentException("The provided fromId is not from same symbol");
                    tradeId = int.Parse(match.Groups[2].Value);
                }
                lock (LockOrdersTrades)
                {
                    var result = Trades.Find(tr => tr.TradeId > tradeId && tr.Symbol == symbol).ToArray<ITrade>();
                    var ret = new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, result);
                    return Task.FromResult<IMarketOperation<IEnumerable<ITrade>>>(ret);
                }
            }
            catch (Exception ex)
            {
                var ret = new MarketOperation<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
                return Task.FromResult<IMarketOperation<IEnumerable<ITrade>>>(ret);
            }
        }

        public Task<IMarketOperation<IEnumerable<ITrade>>> GetAllTradesAsync(string symbol)
        {
            lock (LockOrdersTrades)
            {
                var result = Trades.Find(tr => tr.Symbol == symbol).ToArray<ITrade>();
                return Task.FromResult<IMarketOperation<IEnumerable<ITrade>>>(
                    new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, result));
            }
        }

        public async Task<IMarketOperation<IOrder>> OrderSynchAsync(string id)
        {
            try
            {
                var res = DeconstructId(id);
                var binOrd = await Client.QueryOrder(new QueryOrderRequest() { OrderId = res.id, Symbol = res.symbol });
                var result = new ApiOrder(binOrd);
                OrdersUpdateOrInsert(result);
                OrdersActiveInsertOrUpdate(result);
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public async Task<IMarketOperation<IOrder>> GetOrderAsync(string id)
        {
            try
            {
                var result = Orders.FindOne(o => o.Id == id);
                if (result == null)
                {
                    var res = DeconstructId(id);
                    var binOrd = await Client.QueryOrder(new QueryOrderRequest() { OrderId = res.id, Symbol = res.symbol });
                    result = new ApiOrder(binOrd);
                    OrdersUpdateOrInsert(result);
                }
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public Task<IMarketOperation<ITrade>> GetTradeAsync(string id)
        {
            try
            {
                var result = Trades.FindOne(o => o.Id == id);
                if (result == null)
                {
                    return Task.FromResult<IMarketOperation<ITrade>>(
                        new MarketOperation<ITrade>($"Trade {id} not found"));
                }
                return Task.FromResult<IMarketOperation<ITrade>>(
                    new MarketOperation<ITrade>(MarketOperationStatus.Completed, result));
            }
            catch (Exception ex)
            {
                return Task.FromResult<IMarketOperation<ITrade>>(
                    new MarketOperation<ITrade>(GetExceptionErrorInfo(ex)));
            }
        }

        public decimal GetFreeBalance(string asset)
        {
            lock (LockBalances)
            {
                if (_Balances.ContainsKey(asset))
                    return _Balances[asset].Free;
            }
            return 0;
        }
        public decimal GetTotalBalance(string asset)
        {
            lock (LockBalances)
            {
                if (_Balances.ContainsKey(asset))
                    return _Balances[asset].Free + _Balances[asset].Locked;
            }
            return 0;
        }


        MemoryCache Cache = new MemoryCache();
        public async Task<IMarketOperation<decimal>> GetEquity(string asset)
        {

            try
            {
                List<SymbolPriceResponse> allPrices;
                if (Cache.TryGetValue("allPrices", out object result))
                    allPrices = result as List<SymbolPriceResponse>;
                else
                {
                    allPrices = await Client.GetSymbolsPriceTicker();
                    Cache.Set("allPrices", allPrices, DateTime.Now.AddSeconds(30));
                }

                if (asset == "ETH" || asset == "BNB" || asset == "BTC")
                {
                    lock (LockBalances)
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
                        return new MarketOperation<decimal>(MarketOperationStatus.Completed, val);
                    }
                }
                else
                {
                    var btceq = await GetEquity("BTC");
                    var price1 = allPrices.FirstOrDefault(pri => pri.Symbol == asset + "BTC");
                    var price2 = allPrices.FirstOrDefault(pri => pri.Symbol == "BTC" + asset);
                    if (price1 != null && btceq.Status == MarketOperationStatus.Completed)
                        return new MarketOperation<decimal>(MarketOperationStatus.Completed, (decimal)price1.Price / btceq.Result);
                    else if (price2 != null && btceq.Status == MarketOperationStatus.Completed)
                        return new MarketOperation<decimal>(MarketOperationStatus.Completed, (decimal)price2.Price * btceq.Result);
                    else
                        return new MarketOperation<decimal>("Unable to get the price of the symbol");
                }
            }
            catch (Exception ex)
            {
                Debug.Assert(ex != null);
                return new MarketOperation<decimal>(MarketOperationStatus.Failed);
            }

        }

        public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
        {
            SymbolFeed feed;
            lock (LockBalances)
                feed = Feeds.Where(sf => sf.Symbol.Key == symbol).FirstOrDefault();

            if (feed == null)
            {
                var binanceSymbol = ExchangeInfo.Symbols.FirstOrDefault(s => s.symbol == symbol);
                var lotSize = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterLotSize) as ExchangeInfoSymbolFilterLotSize;
                var minNotional = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterMinNotional) as ExchangeInfoSymbolFilterMinNotional;
                var pricePrecision = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterPrice) as ExchangeInfoSymbolFilterPrice;
                var symInfo = new SymbolInfo()
                {
                    Asset = binanceSymbol.baseAsset,
                    QuoteAsset = binanceSymbol.quoteAsset,
                    Key = binanceSymbol.symbol,
                    IsMarginTadingAllowed = binanceSymbol.isMarginTradingAllowed,
                    IsSpotTadingAllowed = binanceSymbol.isSpotTradingAllowed,
                    LotSizeStep = lotSize.StepSize,
                    MinLotSize = lotSize.MinQty,
                    MinNotional = minNotional.MinNotional,
                    PricePrecision = pricePrecision.TickSize
                };

                feed = new SymbolFeed(Client, CombinedWebSocketClient, HistoryDb, MarketName, symInfo);
                await feed.Initialize();
                lock (LockBalances)
                {
                    Feeds.Add(feed);
                }
            }
            return feed;
        }

        public void DisposeFeed(ISymbolFeed f)
        {
            lock (LockBalances)
            {
                if (f is SymbolFeed feed && Feeds.Contains(feed))
                {
                    Feeds.Remove(feed);
                    feed.Dispose();
                }
            }
        }

        public async Task<IMarketOperation<IOrder>> LimitOrderAsync(string symbol, TradeDirection type, decimal amount, decimal rate, string clientOrderId = null)
        {
            ResultCreateOrderResponse newOrd;
            try
            {

                newOrd = (ResultCreateOrderResponse)await Client.CreateOrder(
                    new CreateOrderRequest()
                    {
                        Symbol = symbol,
                        Side = type == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell,
                        Quantity = amount / 1.00000000000000000000000000m,
                        NewClientOrderId = clientOrderId,
                        NewOrderResponseType = NewOrderResponseType.Result,
                        Price = rate / 1.00000000000000000000000000000m,
                        Type = be.Enums.OrderType.Limit,
                        TimeInForce = TimeInForce.GTC
                    });

                var no = new ApiOrder(newOrd);
                OrdersUpdateOrInsert(no);
                OrdersActiveInsertOrUpdate(no);
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, no);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public async Task<IMarketOperation<IOrder>> MarketOrderAsync(string symbol, TradeDirection type, decimal amount, string clientOrderId = null)
        {
            var side = type == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell;
            try
            {
                var symbolInfo = ExchangeInfo.Symbols.FirstOrDefault(s => s.symbol == symbol);
                ApiOrder newOrd = null;
                var ord = (ResultCreateOrderResponse)await Client.CreateOrder(
                        new CreateOrderRequest()
                        {
                            Symbol = symbol,
                            Side = side,
                            Type = be.Enums.OrderType.Market,
                            Quantity = (decimal)amount / 1.00000000000000m,
                            NewClientOrderId = clientOrderId,
                            NewOrderResponseType = NewOrderResponseType.Result,
                        });

                lock (LockOrdersTrades)
                {
                    newOrd = new ApiOrder(ord);
                    var oldOrd = Orders.FindOne(o => o.Id == newOrd.Id);
                    if (oldOrd != null)
                        newOrd = oldOrd;
                    else
                        Orders.Insert(newOrd);
                }

                lock (LockBalances)
                {
                    var quoteBal = _Balances[symbolInfo.quoteAsset];
                    var assetBal = _Balances[symbolInfo.baseAsset];
                }

                //todo update balance
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, newOrd);

            }
            catch (Exception ex)
            {

                return new MarketOperation<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public async Task<IMarketOperation> OrderCancelAsync(string id)
        {
            try
            {
                ApiOrder order = null;
                order = this.Orders.FindOne(or => or.Id == id);
                if (order != null)
                {
                    var cancel = await this.Client.CancelOrder(new CancelOrderRequest()
                    {
                        Symbol = order.Symbol,
                        OrderId = order.OrderId,
                    });
                    lock (LockBalances)
                    {
                        var symbolInfo = ExchangeInfo.Symbols.FirstOrDefault(s => s.symbol == order.Symbol);
                        var quoteBal = _Balances[symbolInfo.quoteAsset];
                        var assetBal = _Balances[symbolInfo.baseAsset];

                        if (order.TradeType == TradeDirection.Buy)
                        {
                            quoteBal.Free += (order.Amount - order.Filled) * (decimal)order.Price;
                        }
                        else if (order.TradeType == TradeDirection.Sell)
                        {
                            assetBal.Free += order.Amount - order.Filled;
                        }
                        return new MarketOperation<object>(MarketOperationStatus.Completed, order);
                    }
                }
                else
                    return new MarketOperation<object>($"Unknown order {id}");

            }
            catch (Exception ex)
            {
                return new MarketOperation<object>(GetExceptionErrorInfo(ex));
            }
        }

        public (decimal min, decimal step) GetMinTradable(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.LotSize).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterLotSize lotsize)
                    return (lotsize.MinQty, lotsize.StepSize);
            }
            throw new Exception($"Lotsize info not found for symbol {symbol}");
        }

        public decimal GetSymbolPrecision(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.PriceFilter).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterPrice pf)
                    return pf.TickSize;
            }
            throw new Exception($"Precision info not found for symbol {symbol}");
        }

        public IEnumerable<SymbolInfo> GetSymbols()
        {
            return ExchangeInfo.Symbols.Where(sym => sym.status == "TRADING").Select(sym => new SymbolInfo
            {
                Asset = sym.baseAsset,
                QuoteAsset = sym.quoteAsset,
                Key = sym.symbol,
            });
        }

        public decimal GetMinNotional(string symbol)
        {
            var info = ExchangeInfo.Symbols.Where(s => s.symbol == symbol).FirstOrDefault();
            if (info != null)
            {
                var filt = info.filters.Where(f => f.FilterType == ExchangeInfoSymbolFilterType.MinNotional).FirstOrDefault();
                if (filt is ExchangeInfoSymbolFilterMinNotional mn)
                    return mn.MinNotional;
            }
            return 0;
        }

        public void Dispose()
        {
            Trades = null;
            Orders = null;
            TradesAndOrdersDb.Dispose();
            TradesAndOrdersArch.Dispose();
        }

        class MarketOperation<T> : IMarketOperation<T>
        {
            public T Result { get; }
            public string ErrorInfo { get; internal set; }
            public MarketOperationStatus Status { get; internal set; }
            public bool IsSuccessful => Status == MarketOperationStatus.Completed;
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

        class SymbolFeed : ISymbolFeed, IDisposable
        {
            public event Action<ISymbolFeed, IBaseData> OnData;
            private NLog.Logger Logger;
            private BinanceClient Client;
            private CombinedWebSocketClient WebSocketClient;

            private HistoricalRateDataBase HistoryDb;

            private System.Timers.Timer HearthBeatTimer;
            private Stopwatch KlineWatchdog = new Stopwatch();
            private Stopwatch DepthWatchdog = new Stopwatch();

            private DateTime LastKlineWarn = DateTime.Now;
            private DateTime LastDepthWarn = DateTime.Now;
            private BinanceKline FormingCandle = new BinanceKline() { StartTime = DateTime.MaxValue };

            public SymbolInfo Symbol { get; private set; }
            public DateTime Time { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }

            public SymbolFeed(BinanceClient client, CombinedWebSocketClient websocket, HistoricalRateDataBase hist, string market, SymbolInfo symbol)
            {
                HistoryDb = hist;
                this.Client = client;
                this.WebSocketClient = websocket;
                this.Symbol = symbol;
                this.Market = market;
                Logger = LogManager.GetLogger("Bin" + Symbol + "Feed");
            }

            internal async Task Initialize()
            {
                var book = await Client.GetOrderBook(Symbol.Key, false);
                Ask = (double)book.Asks.First().Price;
                Bid = (double)book.Bids.First().Price;
                KlineListen();
                PartialDepthListen();

                HearthBeatTimer = new System.Timers.Timer(2500)
                {
                    AutoReset = false,
                    Enabled = false,
                    Interval = 2500,
                };
                HearthBeatTimer.Elapsed += HearthBeat;
                HearthBeatTimer.Start();
                DepthWatchdog.Restart();
                KlineWatchdog.Restart();
            }

            private void HearthBeat(object state, ElapsedEventArgs args)
            {
                if (KlineWatchdog.ElapsedMilliseconds > 90000)
                {
                    if (DateTime.Now > LastKlineWarn.AddSeconds(90000))
                    {
                        Logger.Warn("Kline websock looked like frozen");
                        LastKlineWarn = DateTime.Now;
                    }
                    KlineListen();
                }
                if (DepthWatchdog.ElapsedMilliseconds > 90000)
                {
                    if (DateTime.Now > LastDepthWarn.AddSeconds(90000))
                    {
                        Logger.Warn("Depth websock looked like frozen");
                        LastDepthWarn = DateTime.Now;
                    }
                    PartialDepthListen();
                }

                HearthBeatTimer.Start();


            }

            private void KlineListen()
            {
                try
                {
                    WebSocketClient.SubscribeKlineStream(this.Symbol.Key.ToLower(), KlineInterval.OneMinute, HandleKlineEvent);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception during KlineListen: " + GetExceptionErrorInfo(ex));
                }
            }

            private void PartialDepthListen()
            {
                try
                {
                    WebSocketClient.SubscribePartialDepthStream(this.Symbol.Key.ToLower(), PartialDepthLevels.Five, HandlePartialDepthUpdate);

                }
                catch (Exception ex)
                {
                    Logger.Error("Exception during PartialDepthListen: " + GetExceptionErrorInfo(ex));
                }

            }

            private void HandlePartialDepthUpdate(BinancePartialData messageData)
            {
                DepthWatchdog.Restart();
                var bid = (double)messageData.Bids.FirstOrDefault(b => b.Quantity > 0).Price;
                var ask = (double)messageData.Asks.FirstOrDefault(a => a.Quantity > 0).Price;
                if (bid != 0 && ask != 0)
                {
                    this.Bid = bid;
                    this.Ask = ask;
                    Spread = Ask - Bid;
                    //call on data
                    this.OnData?.Invoke(this, new QuoteTick(Bid, Ask, messageData.EventTime));
                }
                this.Time = messageData.EventTime;
            }

            private void HandleKlineEvent(BinanceKlineData msg)
            {
                if (FormingCandle != null && msg.Kline.StartTime > FormingCandle.StartTime)
                {
                    //if this tick is a new candle and the last candle was not added to ticks
                    //then let's add it
                    var candle = new Candlestick()
                    {
                        Close = (double)FormingCandle.Close,
                        High = (double)FormingCandle.High,
                        CloseTime = FormingCandle.StartTime.AddSeconds(60),
                        OpenTime = FormingCandle.StartTime,
                        Low = (double)FormingCandle.Low,
                        Open = (double)FormingCandle.Open,
                        Volume = (double)FormingCandle.QuoteVolume
                    };
                    this.OnData?.Invoke(this, candle);
                    FormingCandle = null;
                }


                if (msg.Kline.IsBarFinal)
                {
                    KlineWatchdog.Restart();
                    var candle = new Candlestick()
                    {
                        Close = (double)msg.Kline.Close,
                        High = (double)msg.Kline.High,
                        CloseTime = msg.Kline.StartTime.AddSeconds(60),
                        OpenTime = msg.Kline.StartTime,
                        Low = (double)msg.Kline.Low,
                        Open = (double)msg.Kline.Open,
                        Volume = (double)msg.Kline.QuoteVolume
                    };

                    this.OnData?.Invoke(this, candle);
                    FormingCandle = null;
                }
                else
                {
                    FormingCandle = msg.Kline;
                }
            }

            public Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime)
            {
                return GetHistoryNavigator(TimeSpan.FromMinutes(1), historyStartTime);
            }

            public async Task<TimeSerie<ITradeBar>> GetHistoryNavigator(TimeSpan resolution, DateTime historyStartTime)
            {
                if (resolution != TimeSpan.FromMinutes(1))
                    throw new NotSupportedException("Binance symbol history only supports resolution 1 minute");
                historyStartTime -= TimeSpan.FromMinutes(1);
                //load missing data to hist db 
                Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");

                var historyPeriodSpan = DateTime.UtcNow - historyStartTime;

                //--- download latest data
                var refreshTime = historyPeriodSpan > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : historyPeriodSpan;
                var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb, Client);
                await downloader.DownloadHistoryAsync(Symbol.Key, historyStartTime, refreshTime);

                //--- load the history into this 
                var historyInfo = new HistoryInfo(this.Market, Symbol.Key, TimeSpan.FromSeconds(60));
                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(historyInfo, historyStartTime, DateTime.MaxValue);
                HistoryDb.CloseFile(this.Market, Symbol.Key, TimeSpan.FromSeconds(60));

                var history = new TimeSerie<ITradeBar>();
                while (symbolHistory.Ticks.MoveNext())
                    history.AddRecord(symbolHistory.Ticks.Current, true);

                return history;
            } 

            public void Dispose()
            {
                HearthBeatTimer.Stop();
                HearthBeatTimer.Dispose();
                try
                {
                    WebSocketClient.Unsubscribe<BinanceKlineData>(HandleKlineEvent);
                    WebSocketClient.Unsubscribe<BinancePartialData>(HandlePartialDepthUpdate);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exeption during SymbolFeed.Dispose: " + ex.Message);
                }
            }

            decimal NearestRoundHigher(decimal x, decimal precision)
            {
                if (precision != 0)
                {
                    var resto = x % precision;
                    x = x - resto + precision;
                }
                return x;
            }
            decimal NearestRoundLower(decimal x, decimal precision)
            {
                if (precision != 0)
                {
                    var resto = x % precision;
                    x = x - resto;
                }
                return x;
            }

            public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal amount, decimal price)
            {

                price = NearestRoundHigher(price, this.Symbol.PricePrecision);
                if (amount * price < Symbol.MinNotional)
                    amount = Symbol.MinNotional / price;

                if (amount < Symbol.MinLotSize)
                    amount = Symbol.MinLotSize;

                amount = NearestRoundHigher(amount, Symbol.LotSizeStep);


                return (price / 1.00000000000m, amount / 1.000000000000m);
            }

            public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal amount, decimal price)
            {

                price = NearestRoundLower(price, this.Symbol.PricePrecision);
                if (amount * price < Symbol.MinNotional)
                    amount = 0;

                if (amount < Symbol.MinLotSize)
                    amount = 0;

                amount = NearestRoundLower(amount, Symbol.LotSizeStep);
                 
                return (price / 1.00000000000m, amount / 1.000000000000m);
            }
        }
    }

    class ApiOrder : IOrder
    {
        [BsonId]
        public string Id { get; set; }
        public string Symbol { get; set; }
        public long OrderId { get; set; }
        public decimal Filled { get; set; }
        public string Market { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public string ClientId { get; set; }
        public TradeDirection TradeType { get; set; }
        public OrderType Type { get; set; }
        public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
        public List<long> ResultingTrades { get; set; } = new List<long>();
        public DateTime Time { get; set; }

        public bool IsClosed => Status >= OrderStatus.Cancelled;

        public ApiOrder() { }

        public ApiOrder(AcknowledgeCreateOrderResponse binanceOrder)
        {
            OrderId = binanceOrder.OrderId;
            ClientId = binanceOrder.ClientOrderId;
            Symbol = binanceOrder.Symbol;
            Time = binanceOrder.TransactionTime;
            Market = "Binance";
            Id = Symbol + OrderId;
        }

        public ApiOrder(ResultCreateOrderResponse binanceOrder)
        {
            OrderId = binanceOrder.OrderId;
            ClientId = binanceOrder.ClientOrderId;
            Symbol = binanceOrder.Symbol;
            Market = "Binance";

            TradeType = binanceOrder.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(binanceOrder.Type);
            Amount = binanceOrder.OriginalQuantity;
            Price = binanceOrder.Price;
            Status = GetStatus(binanceOrder.Status);
            Filled = binanceOrder.ExecutedQuantity;
            Id = Symbol + OrderId;
            Time = binanceOrder.TransactionTime;
        }

        public ApiOrder(OrderResponse or)
        {
            Symbol = or.Symbol;
            Market = "Binance";
            TradeType = or.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(or.Type);
            Amount = or.OriginalQuantity;
            Price = or.Price;
            Status = GetStatus(or.Status);
            Filled = or.ExecutedQuantity;
            OrderId = or.OrderId;
            Id = Symbol + OrderId;
            ClientId = or.ClientOrderId;
            Time = or.Time;
        }

        public ApiOrder(BinanceTradeOrderData bo)
        {
            OrderId = bo.OrderId;
            Symbol = bo.Symbol;
            Market = "Binance";
            TradeType = bo.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(bo.Type);
            Amount = bo.Quantity;
            Price = bo.Price;
            Status = GetStatus(bo.OrderStatus);
            Filled = bo.AccumulatedQuantityOfFilledTradesThisOrder;
            Id = Symbol + OrderId;
            ClientId = bo.NewClientOrderId;
            Time = bo.EventTime;
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
            Direction = tr.IsBuyer ? TradeDirection.Buy : TradeDirection.Sell;
            Price = tr.Price;
            Amount = tr.Quantity;
            Commission = tr.Commission;
            CommissionAsset = tr.CommissionAsset;
            Time = tr.Time;
            OrderId = tr.OrderId;
            TradeId = tr.Id;
            Id = Symbol + TradeId;
        }

        public ApiTrade(BinanceTradeOrderData tr)
        {
            Market = "Binance";
            Symbol = tr.Symbol;
            Direction = tr.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Price = tr.PriceOfLastFilledTrade;
            Amount = tr.QuantityOfLastFilledTrade;
            Commission = Commission;
            CommissionAsset = CommissionAsset;
            Time = tr.TimeStamp;
            OrderId = tr.OrderId;
            ClientOrderId = tr.NewClientOrderId;
            TradeId = tr.TradeId;
            Id = Symbol + TradeId;
        }
        [BsonId]
        public string Id { get; set; }
        public long TradeId { get; set; }
        public long OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public decimal Amount { get; set; }
        public decimal Commission { get; set; }
        public string Market { get; set; }
        public decimal Price { get; set; }
        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public string CommissionAsset { get; set; }
        public DateTime Time { get; set; }

        [BsonIgnore]
        string ITrade.OrderId => Symbol + OrderId;
    }

    class AssetBalance
    {
        public string Asset;
        public decimal Free;
        public decimal Locked;
        public decimal Total => Free + Locked;
    }
}
