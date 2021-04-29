
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.Response.Error;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using Newtonsoft.Json.Linq;
using NLog;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using be = BinanceExchange.API;

namespace SharpTrader.BrokersApi.Binance
{
    public partial class BinanceMarketApi : IMarketApi
    {
        class SymbolData
        {
            [BsonId]
            public string Symbol { get; set; }
            public long LastOrderAtTradesSynch { get; set; }
        }



        public event Action<IMarketApi, ITrade> OnNewTrade;

        private readonly object LockOrdersTrades = new object();
        private readonly object LockBalances = new object();
        private readonly List<Order> OpenOrders = new List<Order>();
        private readonly Stopwatch StopwatchSocketClose = new Stopwatch();
        private readonly Stopwatch UserDataPingStopwatch = new Stopwatch();

        private ILiteCollection<SymbolData> SymbolsData;
        private ILiteCollection<Order> DbOpenOrders;
        private ILiteCollection<Order> Orders;
        private ILiteCollection<Trade> Trades;
        private ILiteCollection<Order> OrdersArchive;
        private ILiteCollection<Trade> TradesArchive;
        private BinanceTradeBarsRepository HistoryDb;
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
        private DateTime LastOperationsArchivingTime = DateTime.MinValue;
        private string OperationsDbPath;
        private string OperationsArchivePath;
        private DateTime TimeToArchive;
        private MemoryCache Cache = new MemoryCache();
        private Task FastUpdatesTask;
        private Task OrdersAndTradesSynchTask;
        public DateTime StartOperationDate { get; set; } = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public BinanceClient Client { get; private set; }

        public string MarketName => "Binance";

        public DateTime Time => DateTime.UtcNow.Add(Client.TimestampOffset);

        IEnumerable<ITrade> IMarketApi.Trades => Trades.FindAll().ToArray();

        IEnumerable<IOrder> IMarketApi.OpenOrders { get { lock (LockOrdersTrades) return OpenOrders.ToArray(); } }

        public (string Symbol, decimal balance)[] FreeBalances
        {
            get
            {
                lock (LockBalances)
                    return _Balances.Select(kv => (kv.Key, kv.Value.Free)).ToArray();
            }
        }

        public BinanceMarketApi(string apiKey, string apiSecret, string dataDir, double rateLimitFactor = 1)
        {
            Logger = LogManager.GetLogger("BinanceMarketApi");
            Logger.Info("starting initialization...");
            OperationsDbPath = Path.Combine("Data", "BinanceAccountsData", $"{apiKey}_tnd.db");
            OperationsArchivePath = Path.Combine("Data", "BinanceAccountsData", $"{apiKey}_tnd_archive.db");
            InitializeOperationsDb();
            Client = new BinanceClient(new ClientConfiguration()
            {
                ApiKey = apiKey ?? "null",
                SecretKey = apiSecret ?? "null",
                RateLimitFactor = rateLimitFactor
            });
            this.HistoryDb = new BinanceTradeBarsRepository(dataDir, Client);
        }

        public async Task Initialize(bool resynchTradesAndOrders = false, bool publicOnly = false)
        {
            ExchangeInfo = await Client.GetExchangeInfo();
            WSClient = new BinanceWebSocketClient(Client);
            CombinedWebSocketClient = new CombinedWebSocketClient();
            await this.ServerTimeSynch();
            Logger.Info("Archiving old oders...");
            ArchiveOldOperations();

            if (resynchTradesAndOrders)
                await DownloadAllOrdersAndTrades();
            else
            {
                Logger.Info("Synching oders and trades....");
                if (!publicOnly)
                {
                    await SynchOpenOrders();
                    await SynchLastTrades();
                }
            }

            if (!publicOnly)
            {
                await ListenUserData();
                await SynchBalance();

                TimerListenUserData = new System.Timers.Timer(30000)
                {
                    AutoReset = false,
                    Enabled = true,
                };
                TimerListenUserData.Elapsed += (s, ea) => ListenUserData().ContinueWith(t => TimerListenUserData.Start());

                //----------

                async Task SynchOrdersAndTrades()
                {
                    while (true)
                    {
                        try
                        {
                            //we first synch open orders 
                            await SynchOpenOrders();
                            //synch last trades 
                            await SynchLastTrades();
                            //then finally we restart the timer and archive operations  
                            ArchiveOldOperations();
                        }
                        catch { }
                        await Task.Delay(TimeSpan.FromSeconds(60));

                    }
                };
                OrdersAndTradesSynchTask = Task.Run(SynchOrdersAndTrades);
            }

            FastUpdatesTask =
                Task.Run(
                    async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                //first synch server time then balance then restart timer
                                if (!publicOnly)
                                    await SynchBalance();
                                await ServerTimeSynch();
                            }
                            catch { }
                            await Task.Delay(15000);
                        }
                    });
            Logger.Info("initialization complete");
        }

        private void ArchiveOldOperations()
        {

            if (LastOperationsArchivingTime + TimeSpan.FromHours(24) < DateTime.Now)
                lock (LockOrdersTrades)
                {
                    TimeToArchive = Time - TimeSpan.FromDays(30);
                    List<Order> ordersToMove = Orders.Find(o => o.Time < TimeToArchive).ToList();
                    List<Trade> tradesToMove = new List<Trade>();
                    foreach (var order in ordersToMove)
                    {
                        tradesToMove.AddRange(Trades.Find(t => t.OrderId == order.OrderId && t.Symbol == order.Symbol).ToArray());
                    }
                    foreach (var trade in tradesToMove.ToArray())
                    {
                        Trades.Delete(trade.Id);
                        if (TradesArchive.FindById(trade.Id) != null)
                            tradesToMove.Remove(trade);

                    }

                    foreach (var ord in ordersToMove.ToArray())
                    {
                        Orders.Delete(ord.Id);
                        if (OrdersArchive.FindById(ord.Id) != null)
                            ordersToMove.Remove(ord);
                    }


                    TradesArchive.Insert(tradesToMove);
                    OrdersArchive.Insert(ordersToMove);

                    TradesAndOrdersDb.Rebuild();
                    LastOperationsArchivingTime = DateTime.Now;
                    TradesAndOrdersDb.Dispose();
                    TradesAndOrdersArch.Dispose();
                    InitializeOperationsDb();
                }
        }

        private void InitializeOperationsDb()
        {

            if (!Directory.Exists(Path.GetDirectoryName(OperationsDbPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(OperationsDbPath));

            //----
            TradesAndOrdersArch = new LiteDatabase(OperationsArchivePath);
            TradesAndOrdersArch.Pragma("UTC_DATE", true);
            TradesAndOrdersDb = new LiteDatabase(OperationsDbPath);
            TradesAndOrdersArch.Pragma("UTC_DATE", true);

            var dbs = new[] { TradesAndOrdersDb, TradesAndOrdersArch };
            foreach (var db in dbs)
            {
                var orders = db.GetCollection<Order>("Orders");
                orders.EnsureIndex(o => o.Id, true);
                orders.EnsureIndex(o => o.OrderId);
                orders.EnsureIndex(o => o.Symbol);
                orders.EnsureIndex(o => o.Filled);
                orders.EnsureIndex(o => o.Status);

                var trades = db.GetCollection<Trade>("Trades");
                trades.EnsureIndex(o => o.Id, true);
                trades.EnsureIndex(o => o.TradeId);
                trades.EnsureIndex(o => o.Symbol);
                trades.EnsureIndex(o => o.OrderId);
                trades.EnsureIndex(o => o.TradeId);
            }

            //----
            DbOpenOrders = TradesAndOrdersDb.GetCollection<Order>("OpenOrders");
            Orders = TradesAndOrdersDb.GetCollection<Order>("Orders");
            Trades = TradesAndOrdersDb.GetCollection<Trade>("Trades");
            SymbolsData = TradesAndOrdersDb.GetCollection<SymbolData>("SymbolsData");

            OrdersArchive = TradesAndOrdersArch.GetCollection<Order>("Orders");
            TradesArchive = TradesAndOrdersArch.GetCollection<Trade>("Trades");
        }

        private async Task DownloadAllOrdersAndTrades()
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
            List<Order> orders = new List<Order>();
            while (!finish)
            {
                var responses = await Client.GetAllOrders(new AllOrdersRequest() { OrderId = start, Symbol = sym });
                var toInsert = responses.Select(or => new Order(or)).OrderBy(o => o.OrderId).ToArray();
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
            List<Trade> trades = new List<Trade>();
            while (!finish)
            {
                var responses = await Client.GetAccountTrades(new AllTradesRequest() { FromId = start, Symbol = sym });
                var toInsert = responses.Select(or => new Trade(sym, or)).OrderBy(o => o.TradeId).ToArray();
                foreach (var tr in toInsert)
                {
                    var order = Orders.FindOne(o => o.OrderId == tr.OrderId);
                    if (order == null)
                        order = OrderSynchAsync(tr.Symbol + tr.OrderId).Result.Result as Order;
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
                Client.TimestampOffset = time.ServerTime - DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error("Error during server time synch: " + GetExceptionErrorInfo(ex));
            }

        }

        private async Task SynchBalance()
        {
            try
            {
                var accountInfo = await Client.GetAccountInformation();
                lock (LockBalances)
                {
                    foreach (var bal in accountInfo.Balances)
                    {
                        if (!_Balances.ContainsKey(bal.Asset))
                            this._Balances[bal.Asset] = new AssetBalance();

                        this._Balances[bal.Asset].Asset = bal.Asset;
                        this._Balances[bal.Asset].Free = bal.Free;
                        this._Balances[bal.Asset].Locked = bal.Locked;

                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error while trying to update account info: " + GetExceptionErrorInfo(ex));
            }


        }

        private void RemoveOpenOrder(Order order)
        {
            DbOpenOrders.Delete(order.Id);
            OpenOrders.Remove(order);
        }

        /// <summary>
        /// Synchronizes all open orders
        /// </summary> 
        private async Task SynchOpenOrders()
        {
            try
            {
                var resp = await Client.GetCurrentOpenOrders(new CurrentOpenOrdersRequest() { Symbol = null });
                var allOpen = resp.Select(o => new Order(o));
                Order[] ordersClosed;
                lock (LockOrdersTrades)
                {
                    foreach (var newOrder in allOpen)
                        OrdersActiveInsertOrUpdate(newOrder);

                    ordersClosed = OpenOrders.Where(oo => !allOpen.Any(no => no.Id == oo.Id)).ToArray();
                }

                foreach (var ord in ordersClosed)
                {
                    try
                    {
                        if (!ord.IsClosed)
                        {
                            //the order is out of synch as we think it's still open but was actually closed
                            var orderUpdated =
                                new Order(await Client.QueryOrder(new QueryOrderRequest() { OrderId = ord.OrderId, Symbol = ord.Symbol }));
                            ord.Update(orderUpdated);
                            OrdersUpdateOrInsert(orderUpdated);
                        }
                        RemoveOpenOrder(ord);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error during order {ord.ToString()} synchronization: " + GetExceptionErrorInfo(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error during orders synchronization: " + GetExceptionErrorInfo(ex));
            }
        }
        private async Task SynchLastTrades()
        {
            foreach (var sym in ExchangeInfo.Symbols.Select(s => s.symbol))
                await SynchLastTrades(sym);
        }
        public async Task SynchLastTrades(string sym, bool force = false)
        {
            var symData = SymbolsData.FindById(sym);
            if (symData == null)
            {
                symData = new SymbolData { Symbol = sym };
                SymbolsData.Insert(symData);
            }


            if (sym == null)
                throw new Exception("Parameter sym cannot be null");
            try
            {
                bool hasActiveOrders = false;
                lock (LockOrdersTrades)
                    hasActiveOrders = OpenOrders.Any(o => o.Symbol == sym);

                var sOrders = Orders.Find(o => o.Symbol == sym).OrderBy(o => o.OrderId);
                var lastOrder = sOrders.LastOrDefault();

                if (lastOrder != null)
                {
                    if (symData.LastOrderAtTradesSynch != lastOrder.OrderId || hasActiveOrders || force)
                    {
                        var resp = await Client.GetAccountTrades(new AllTradesRequest { Symbol = sym, Limit = 100 });
                        var trades = resp.Select(tr => new Trade(sym, tr));
                        foreach (var tr in trades)
                        {
                            //ignore trades older than the StartOfOperation
                            if (tr.Time >= StartOperationDate)
                            {
                                Order order;
                                lock (LockOrdersTrades)
                                {
                                    order = Orders.FindOne(o => o.OrderId == tr.OrderId && o.Symbol == tr.Symbol);
                                    if (order == null)
                                        order = OrdersArchive.FindOne(o => o.OrderId == tr.OrderId && o.Symbol == tr.Symbol);
                                }
                                // put out of lokc to prevent deadlock
                                if (order == null)
                                    order = OrderSynchAsync(tr.Symbol + tr.OrderId).Result.Result as Order;
                                tr.ClientOrderId = order.ClientId;

                                lock (LockOrdersTrades)
                                    TradesUpdateOrInsert(tr);
                            }
                        }
                        //if has active orders we want it to continue updating when the active order becomes inactive
                        if (!hasActiveOrders)
                        {
                            symData.LastOrderAtTradesSynch = lastOrder.OrderId;
                            SymbolsData.Update(symData);
                        }

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
                        TradeUpdateMessageHandler = HandleTradeUpdateMsg,
                        OutboundAccountPositionHandler = HandleOutboundAccountPosition

                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to listen for user data stream because: " + GetExceptionErrorInfo(ex));
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

        private void HandleOutboundAccountPosition(OutboundAccountPosition msg)
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
            var newOrder = new Order(msg);
            //update or add in database 
            OrdersUpdateOrInsert(newOrder);
            OrdersActiveInsertOrUpdate(newOrder);
        }

        private void HandleTradeUpdateMsg(BinanceTradeOrderData msg)
        {
            var tradeUpdate = new Trade(msg);
            var newOrder = new Order(msg);
            //get the unique instance of the order
            newOrder = OrdersActiveInsertOrUpdate(newOrder);
            //add trade to order
            if (!newOrder.ResultingTrades.Contains(tradeUpdate.TradeId))
                newOrder.ResultingTrades.Add(tradeUpdate.TradeId);
            //reinsert the order
            OrdersUpdateOrInsert(newOrder);
            OrdersActiveInsertOrUpdate(newOrder);
            //insert the trade
            TradesUpdateOrInsert(tradeUpdate);
        }

        private void OrdersUpdateOrInsert(Order newOrder)
        {
            lock (LockOrdersTrades)
            {
                if (newOrder.Time < TimeToArchive)
                    //the order is archived or we should archive it
                    OrdersArchive.Upsert(newOrder);
                else
                    Orders.Upsert(newOrder);
            }
        }

        private Order OrdersActiveInsertOrUpdate(Order newOrder)
        {
            Order finalOrder;
            lock (LockOrdersTrades)
            {
                //first we check if we already have an instance of this order
                finalOrder = OpenOrders.FirstOrDefault(oo => oo.Id == newOrder.Id);
                if (finalOrder != null)
                {
                    finalOrder.Update(newOrder);
                    if (finalOrder.IsClosed)
                    {
                        OpenOrders.Remove(newOrder);
                        DbOpenOrders.Delete(newOrder.Id);
                    }
                    else
                        DbOpenOrders.Upsert(newOrder);
                }
                else
                {
                    finalOrder = newOrder;
                    if (!finalOrder.IsClosed)
                    {
                        OpenOrders.Add(finalOrder);
                        DbOpenOrders.Upsert(finalOrder);
                    }
                }
            }
            return finalOrder;
        }

        private void TradesUpdateOrInsert(Trade newTrade)
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
                    Logger.Debug($"New trade {newTrade}");
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

        public static string GetExceptionErrorInfo(Exception ex)
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
        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(DateTime fromTime)
        {
            try
            {
                lock (LockOrdersTrades)
                {
                    var result = Trades.Find(tr => tr.Time >= fromTime).ToArray<ITrade>();
                    var ret = new Request<IEnumerable<ITrade>>(RequestStatus.Completed, result);
                    return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
                }
            }
            catch (Exception ex)
            {
                var ret = new Request<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
                return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
            }
        }

        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, DateTime fromTime)
        {
            try
            {
                lock (LockOrdersTrades)
                {
                    var result = Trades.Find(tr => tr.Time >= fromTime && tr.Symbol == symbol).ToArray<ITrade>();
                    var ret = new Request<IEnumerable<ITrade>>(RequestStatus.Completed, result);
                    return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
                }
            }
            catch (Exception ex)
            {
                var ret = new Request<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
                return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
            }
        }
        public Task<IRequest<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId)
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
                    var ret = new Request<IEnumerable<ITrade>>(RequestStatus.Completed, result);
                    return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
                }
            }
            catch (Exception ex)
            {
                var ret = new Request<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
                return Task.FromResult<IRequest<IEnumerable<ITrade>>>(ret);
            }
        }

        public Task<IRequest<IEnumerable<ITrade>>> GetAllTradesAsync(string symbol)
        {
            lock (LockOrdersTrades)
            {
                var result = Trades.Find(tr => tr.Symbol == symbol).ToArray<ITrade>();
                return Task.FromResult<IRequest<IEnumerable<ITrade>>>(
                    new Request<IEnumerable<ITrade>>(RequestStatus.Completed, result));
            }
        }

        public async Task<IRequest<IOrder>> OrderSynchAsync(string id)
        {
            try
            {
                var res = DeconstructId(id);
                var binOrd = await Client.QueryOrder(new QueryOrderRequest() { OrderId = res.id, Symbol = res.symbol });
                var newOrder = new Order(binOrd);
                newOrder = OrdersActiveInsertOrUpdate(newOrder);
                OrdersUpdateOrInsert(newOrder);
                return new Request<IOrder>(RequestStatus.Completed, newOrder);
            }
            catch (Exception ex)
            {
                return new Request<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public async Task<IRequest<IOrder>> GetKnownOrder(string id)
        {
            try
            {
                var result = Orders.FindOne(o => o.Id == id);
                if (result == null)
                {
                    var res = DeconstructId(id);
                    var binOrd = await Client.QueryOrder(new QueryOrderRequest() { OrderId = res.id, Symbol = res.symbol });
                    result = new Order(binOrd);
                    result = OrdersActiveInsertOrUpdate(result);
                    OrdersUpdateOrInsert(result);
                }
                return new Request<IOrder>(RequestStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new Request<IOrder>(GetExceptionErrorInfo(ex));
            }
        }

        public Task<IRequest<ITrade>> GetTradeAsync(string id)
        {
            try
            {
                var result = Trades.FindOne(o => o.Id == id);
                if (result == null)
                {
                    return Task.FromResult<IRequest<ITrade>>(
                        new Request<ITrade>($"Trade {id} not found"));
                }
                return Task.FromResult<IRequest<ITrade>>(
                    new Request<ITrade>(RequestStatus.Completed, result));
            }
            catch (Exception ex)
            {
                return Task.FromResult<IRequest<ITrade>>(
                    new Request<ITrade>(GetExceptionErrorInfo(ex)));
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

        public async Task<IRequest<decimal>> GetEquity(string asset)
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
                        return new Request<decimal>(RequestStatus.Completed, val);
                    }
                }
                else
                {
                    var btceq = await GetEquity("BTC");
                    var price1 = allPrices.FirstOrDefault(pri => pri.Symbol == asset + "BTC");
                    var price2 = allPrices.FirstOrDefault(pri => pri.Symbol == "BTC" + asset);
                    if (price1 != null && btceq.Status == RequestStatus.Completed)
                        return new Request<decimal>(RequestStatus.Completed, (decimal)price1.Price / btceq.Result);
                    else if (price2 != null && btceq.Status == RequestStatus.Completed)
                        return new Request<decimal>(RequestStatus.Completed, (decimal)price2.Price * btceq.Result);
                    else
                        return new Request<decimal>("Unable to get the price of the symbol");
                }
            }
            catch (Exception ex)
            {
                Debug.Assert(ex != null);
                return new Request<decimal>(RequestStatus.Failed);
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

                feed = new SymbolFeed(Client, CombinedWebSocketClient, HistoryDb, MarketName, symInfo, this.Time);
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
        public async Task<IRequest<IOrder>> PostNewOrder(OrderInfo orderInfo)
        {
            Order newApiOrder = null;
            try
            {
                if (orderInfo.Type == OrderType.Market)
                {
                    orderInfo.Price = null;
                    orderInfo.TimeInForce = null;
                }
                if (orderInfo.Effect == MarginOrderEffect.None)
                {
                    ResultCreateOrderResponse newOrd = (ResultCreateOrderResponse)await Client.CreateOrder(
                        new CreateOrderRequest()
                        {
                            Symbol = orderInfo.Symbol,
                            Side = orderInfo.Direction == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell,
                            Quantity = orderInfo.Amount / 1.00000000000000000000000000m,
                            NewClientOrderId = orderInfo.ClientOrderId,
                            NewOrderResponseType = NewOrderResponseType.Result,
                            Price = orderInfo.Price / 1.00000000000000000000000000000m,
                            Type = GetOrderType(orderInfo.Type),
                            TimeInForce = GetTimeInForce(orderInfo.TimeInForce),
                        });

                    newApiOrder = new Order(newOrd);

                }
                else
                {
                    PostMarginOrderResponse_Result newOrd = (PostMarginOrderResponse_Result)await Client.PostMarginOrder(
                        new PostMarginOrderRequest()
                        {
                            symbol = orderInfo.Symbol,
                            side = orderInfo.Direction == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell,
                            quantity = orderInfo.Amount / 1.00000000000000000000000000m,
                            newClientOrderId = orderInfo.ClientOrderId,
                            newOrderRespType = NewOrderResponseType.Result,
                            price = orderInfo.Price / 1.00000000000000000000000000000m,
                            type = GetOrderType(orderInfo.Type),
                            sideEffectType = GetMarginOrderEffect(orderInfo.Effect),
                            timeInForce = GetTimeInForce(orderInfo.TimeInForce)
                        });
                    newApiOrder = new Order(newOrd);
                }
                newApiOrder = OrdersActiveInsertOrUpdate(newApiOrder);
                OrdersUpdateOrInsert(newApiOrder);
                return new Request<IOrder>(RequestStatus.Completed, newApiOrder);
            }
            catch (Exception ex)
            {
                return new Request<IOrder>(GetExceptionErrorInfo(ex));
            }


        }

        private be.Enums.TimeInForce? GetTimeInForce(TimeInForce? tif)
        {
            if (tif == null)
                return null;
            if (tif == TimeInForce.GTC)
                return be.Enums.TimeInForce.GTC;
            else
                return be.Enums.TimeInForce.IOC;
        }

        private SideEffectType GetMarginOrderEffect(MarginOrderEffect effect)
        {
            if (effect == MarginOrderEffect.ClosePosition)
                return SideEffectType.AUTO_REPAY;
            else if (effect == MarginOrderEffect.OpenPosition)
                return SideEffectType.MARGIN_BUY;
            else
                return SideEffectType.NO_SIDE_EFFECT;
        }

        readonly Dictionary<OrderType, be.Enums.OrderType> OrderTypesLookupTable = new Dictionary<OrderType, be.Enums.OrderType>()
        {
            { OrderType.Limit, be.Enums.OrderType.Limit},
            { OrderType. Market, be.Enums.OrderType.Market},
            { OrderType.StopLoss, be.Enums.OrderType.StopLoss},
            { OrderType.StopLossLimit, be.Enums.OrderType.StopLossLimit},
            { OrderType.TakeProfit, be.Enums.OrderType.TakeProfit},
            { OrderType.TakeProfitLimit, be.Enums.OrderType.TakeProfitLimit},
            { OrderType.LimitMaker, be.Enums.OrderType.LimitMaker},
        };

        private be.Enums.OrderType GetOrderType(OrderType type)
        {
            return OrderTypesLookupTable[type];
        }

        public async Task<IRequest> OrderCancelAsync(string id)
        {
            try
            {
                Order order = null;
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
                        return new Request<object>(RequestStatus.Completed, order);
                    }
                }
                else
                    return new Request<object>($"Unknown order {id}");

            }
            catch (Exception ex)
            {
                return new Request<object>(GetExceptionErrorInfo(ex));
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

        public ITrade GetTradeById(string tradeId)
        {
            return Trades.FindById(tradeId);
        }

        public IOrder GetOrderById(string orderId)
        {
            return Orders.FindById(orderId);
        }

        public ITrade GetTradeById(JToken tradeId)
        {
            throw new NotImplementedException();
        }

        public void RegisterCustomSerializers(BsonMapper mapper)
        {
            BsonMapper defaultMapper = new BsonMapper();
            Order BsonToOrder(BsonValue value)
            {
                lock (LockOrdersTrades)
                {
                    var order = OpenOrders.FirstOrDefault(o => o.Id == value["_id"].AsString);
                    if (order == null)
                        order = Orders.FindById(value["_id"].AsString);
                    if (order == null)
                        order = defaultMapper.Deserialize<Order>(value);
                    if (order != null)
                        OpenOrders.Add(order);
                    return order;
                }
            }
            mapper.Entity<Order>().Ctor(BsonToOrder);

            Trade DeserializeTrade(BsonValue value)
            {
                Trade result = null;
                lock (LockOrdersTrades)
                    result = Trades.FindById(value["_id"].AsString);
                if (result == null)
                    result = defaultMapper.Deserialize<Trade>(value);
                return result;
            }
            mapper.Entity<Trade>().Ctor(DeserializeTrade);
        }


        class Request<T> : IRequest<T>
        {
            public T Result { get; }
            public string ErrorInfo { get; internal set; }
            public RequestStatus Status { get; internal set; }
            public bool IsSuccessful => Status == RequestStatus.Completed;
            public Request(RequestStatus status)
            {
                Status = status;
            }

            public Request(RequestStatus status, T res)
            {
                Status = status;
                Result = res;
            }

            public Request(string errorInfo)
            {
                this.Status = RequestStatus.Failed;
                ErrorInfo = errorInfo;
            }
        }
    }


}
