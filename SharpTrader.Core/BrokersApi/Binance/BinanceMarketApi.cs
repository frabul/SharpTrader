
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.Response.Error;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using NLog;
using SharpTrader.AlgoFramework;
using SharpTrader.Core.BrokersApi.Binance;
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
        private readonly Stopwatch StopwatchSocketClose = new Stopwatch();
        private readonly Stopwatch UserDataPingStopwatch = new Stopwatch();
        private HashSet<Order> OpenOrders = new HashSet<Order>();

        private ILiteCollection<SymbolData> SymbolsData;
        private ILiteCollection<Order> DbOpenOrders;
        private ILiteCollection<Order> Orders;
        private ILiteCollection<Trade> Trades;
        private ILiteCollection<Order> OrdersArchive;
        private ILiteCollection<Trade> TradesArchive;
        public BinanceTradeBarsRepository HistoryDb { get; set; }
        private BinanceWebSocketClient WSClient;
        private CombinedWebSocketClient CombinedWebSocketClient;

        private NLog.Logger Logger;
        private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
        private System.Timers.Timer TimerListenUserData;
        private LiteDatabase TradesAndOrdersArch;
        private LiteDatabase TradesAndOrdersDb;
        private List<SymbolFeed> Feeds = new List<SymbolFeed>();
        private Guid UserDataSocket;
        private Regex IdRegex = new Regex("([0-9A-Z]+[A-Z])([0-9]+)", RegexOptions.Compiled);
        private DateTime LastOperationsArchivingTime = DateTime.MinValue;
        private string OperationsDbPath;
        private string OperationsArchivePath;
        private DateTime TimeToArchive;
        private MemoryCache Cache = new MemoryCache();
        private Task FastUpdatesTask;
        private Task OrdersAndTradesSynchTask;

        private Dictionary<string, BinanceSymbolInfo> Symbols = new Dictionary<string, BinanceSymbolInfo>();
        private Dictionary<string, List<BinanceSymbolInfo>> SymbolsByAsset = new Dictionary<string, List<BinanceSymbolInfo>>();
        private Dictionary<string, List<BinanceSymbolInfo>> SymbolsByQuoteAsset = new Dictionary<string, List<BinanceSymbolInfo>>();

        public DateTime StartOperationDate { get; set; } = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public BinanceClient Client { get; private set; }

        public string MarketName => "Binance";

        public DateTime Time => DateTime.UtcNow.Add(Client.TimestampOffset);
        public TimeSpan OperationsKeepAliveTime { get; set; } = TimeSpan.FromDays(15);
        public TimeSpan BalanceAndTimeSynchPeriod { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan TradesAndOrdersSynchPeriod { get; set; } = TimeSpan.FromSeconds(120);
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
            var exchangeInfo = await Client.GetExchangeInfo();
            //---- initialize symbols dictionary ---
            foreach (var binanceSymbol in exchangeInfo.Symbols)
            {
                BinanceSymbolInfo symInfo = new BinanceSymbolInfo(binanceSymbol);

                if (!SymbolsByAsset.ContainsKey(symInfo.Asset))
                    SymbolsByAsset.Add(symInfo.Asset, new List<BinanceSymbolInfo>());

                if (!SymbolsByQuoteAsset.ContainsKey(symInfo.QuoteAsset))
                    SymbolsByQuoteAsset.Add(symInfo.QuoteAsset, new List<BinanceSymbolInfo>());

                Symbols.Add(symInfo.Key, symInfo);
                SymbolsByAsset[symInfo.Asset].Add(symInfo);
                SymbolsByQuoteAsset[symInfo.QuoteAsset].Add(symInfo);
            }
            //initialize websocket
            WSClient = new BinanceWebSocketClient(Client);
            CombinedWebSocketClient = new CombinedWebSocketClient();
            await this.ServerTimeSynch();

            //initialize db
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
                        await Task.Delay(TradesAndOrdersSynchPeriod);
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
                    }
                };
                OrdersAndTradesSynchTask = Task.Run(SynchOrdersAndTrades);
            }
            async Task SynchTimeAndBalance()
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
                    await Task.Delay(BalanceAndTimeSynchPeriod);
                }
            };
            FastUpdatesTask = Task.Run(SynchTimeAndBalance);
            Logger.Info("initialization complete");
        }


        private void ArchiveOldOperations()
        {

            if (LastOperationsArchivingTime + TimeSpan.FromHours(24) < DateTime.Now)
                lock (LockOrdersTrades)
                {
                    TimeToArchive = Time - OperationsKeepAliveTime;
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
            OpenOrders = new HashSet<Order>(DbOpenOrders.FindAll());
            SymbolsData = TradesAndOrdersDb.GetCollection<SymbolData>("SymbolsData");

            OrdersArchive = TradesAndOrdersArch.GetCollection<Order>("Orders");
            TradesArchive = TradesAndOrdersArch.GetCollection<Trade>("Trades");
        }

        private async Task DownloadAllOrdersAndTrades()
        {
            var symbols = Symbols.Keys.ToArray();
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

        public async Task SynchSymbolOrders(string sym)
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
                        Logger.Error($"Error during order synchronization, {ord.ToString()}, binance id: {ord.OrderId}, error: " + GetExceptionErrorInfo(ex));
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
            foreach (var sym in Symbols.Keys)
                await SynchLastTrades(sym);
        }
        public async Task SynchLastTrades(string sym, bool force = false, int tradesCountLimit = 100)
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

                var pastOrders = Orders.Find(o => o.Symbol == sym).OrderBy(o => o.OrderId);
                var lastOrderClosed = pastOrders.LastOrDefault(o => o.IsClosed);
                var lastOrder = pastOrders.LastOrDefault();
                var lastOrderOpen = OpenOrders.LastOrDefault(o => o.Symbol == sym);

                if (lastOrder != null || force)
                {
                    if (force || symData.LastOrderAtTradesSynch != lastOrder.OrderId || hasActiveOrders)
                    {
                        var resp = await Client.GetAccountTrades(new AllTradesRequest { Symbol = sym, Limit = tradesCountLimit });
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

                        //if has active orders want to continue update from that id until it's closed
                        if (lastOrderOpen != null)
                        {
                            symData.LastOrderAtTradesSynch = lastOrderOpen.OrderId - 1;
                            SymbolsData.Update(symData);
                        }
                        else if (lastOrderClosed != null)
                        {
                            symData.LastOrderAtTradesSynch = lastOrderClosed.OrderId;
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
        public IEnumerable<AssetBalance> GetAllBalances()
        {
            lock (LockBalances)
            {
                return _Balances.Values.ToArray();
            }
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
        /// <summary>
        /// Return
        /// </summary>
        /// <param name="conversionTargetAsset"></param>
        /// <returns>list of anonymous class instances { asset, free, locked, freeConverted, lockedConverted}</returns>
        public async Task<List<AssetBalance>> GetAllBalancesConvertedAsync(string conversionTargetAsset)
        {
            var midAsset = "BTC";


            //get all prices 
            List<AssetBalance> list = new List<AssetBalance>();
            var allPrices = from p in (await this.GetAllPrices())
                            let symInfo = this.GetSymbolInfo(p.Symbol)
                            select
                                new { symKey = p.Symbol, symbol = symInfo, price = p.Price };
            var badSymbols = allPrices.Where(p => p.symbol == null).Select(p=>p.symKey);
            allPrices = allPrices.Where(p => p.symbol != null);

            if(badSymbols.Any()) 
                Logger.Warn("It was not possible to find symbol info for {0}.", string.Join(", ", badSymbols));
            
             

            //calculate the value of target asset compared to BTC
            decimal btcToFinal = 1;
            if (conversionTargetAsset != midAsset)
            {
                var case1 = allPrices.FirstOrDefault(p => p.symbol.Asset == midAsset && p.symbol.QuoteAsset == conversionTargetAsset);
                if (case1 != null)
                    btcToFinal = case1.price;
                else
                {
                    var case2 = allPrices.FirstOrDefault(p => p.symbol.QuoteAsset == midAsset && p.symbol.Asset == conversionTargetAsset);
                    if (case2 == null)
                        throw new Exception($"It is not possible to determine the value of {conversionTargetAsset}");
                    btcToFinal = 1 / case2.price;
                }
            }

            //
            foreach (var bal in this.GetAllBalances())
            {
                var convert = btcToFinal;

                if (bal.Asset != "BTC")
                {
                    var toBtc = allPrices.FirstOrDefault(p => p.symbol.Asset == bal.Asset && p.symbol.QuoteAsset == midAsset);
                    if (toBtc != null)
                        convert = convert * toBtc.price;
                    else
                    {
                        var fromBtc = allPrices.FirstOrDefault(p => p.symbol.QuoteAsset == bal.Asset && p.symbol.Asset == midAsset);
                        if (fromBtc != null)
                            convert = convert / fromBtc.price;
                        else
                            convert = 0;
                    }
                }
                var bb = new AssetBalance { Asset = bal.Asset, Free = bal.Free * convert, Locked = bal.Locked * convert };
                list.Add(bb);
            }
            return list;
        }
        public async Task<IRequest<decimal>> GetEquity(string asset)
        {
            try
            {
                var balancesConverted = await GetAllBalancesConvertedAsync(asset);
                return new Request<decimal>(RequestStatus.Completed, balancesConverted.Sum(b => b.Total));
            }
            catch (Exception ex)
            {
                Logger.Error("Error during GetEquity\n   Message: {0}\n\t   Stack:{1}", ex.Message, ex.StackTrace);
                return new Request<decimal>(RequestStatus.Failed);
            }

        }

        public async Task<List<SymbolPriceResponse>> GetAllPrices()
        {
            List<SymbolPriceResponse> allPrices;
            if (Cache.TryGetValue("allPrices", out object result))
                allPrices = result as List<SymbolPriceResponse>;
            else
            {
                allPrices = await Client.GetSymbolsPriceTicker();
                Cache.Set("allPrices", allPrices, DateTime.Now.AddSeconds(30));
            }

            return allPrices;
        }

        public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
        {
            SymbolFeed feed;
            lock (LockBalances)
                feed = Feeds.Where(sf => sf.Symbol.Key == symbol).FirstOrDefault();

            if (feed == null)
            {
                if (Symbols.ContainsKey(symbol))
                {
                    var symInfo = Symbols[symbol];
                    feed = new SymbolFeed(Client, CombinedWebSocketClient, HistoryDb, MarketName, symInfo, this.Time);
                    await feed.Initialize();
                    lock (LockBalances)
                    {
                        Feeds.Add(feed);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Symbol not found.");
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

                // lock the balance in advance
                lock (LockBalances)
                {
                    var symbol = Symbols[newApiOrder.Symbol];
                    //amount to lock =
                    if (newApiOrder.TradeType == TradeDirection.Buy)
                    {
                        var amountToLock = newApiOrder.Amount * newApiOrder.Price;
                        var quoteBal = this._Balances[symbol.QuoteAsset];
                        quoteBal.Free -= amountToLock;
                        quoteBal.Locked += amountToLock;
                    }
                    else
                    {
                        var amountToLock = newApiOrder.Amount;
                        var assetBal = this._Balances[symbol.Asset];
                        assetBal.Free -= amountToLock;
                        assetBal.Locked += amountToLock;
                    }
                }
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
                        var symbolInfo = Symbols[order.Symbol];
                        var quoteBal = _Balances[symbolInfo.QuoteAsset];
                        var assetBal = _Balances[symbolInfo.Asset];

                        //if (order.TradeType == TradeDirection.Buy)
                        //{
                        //    quoteBal.Free += (order.Amount - order.Filled) * (decimal)order.Price;
                        //}
                        //else if (order.TradeType == TradeDirection.Sell)
                        //{
                        //    assetBal.Free += order.Amount - order.Filled;
                        //}
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
        public async Task<ConvertDustResponse> DustConvert()
        {
            List<AssetBalance> allBalances = await this.GetAllBalancesConvertedAsync("BTC");

            var dustAssets = allBalances.Where(bal => bal.Asset != "BNB" && (bal.Free + bal.Locked) > 0 && (bal.Free + bal.Locked) < 0.0009m).Select(bal => bal.Asset as string).ToList();

            List<string> finalAssets = new List<string>();
            foreach (var asset in dustAssets)
            {
                var sym = asset + "BTC";
                var info = this.GetSymbolInfo(sym);
                if (info?.IsTradingEnabled == true)
                    finalAssets.Add(asset);
            }
            if (finalAssets.Count > 0)
            {
                var pars = new ConvertDustRequest(finalAssets);
                var res = await this.Client.ConvertDustToBNB(pars);
                return res;
            }
            return new ConvertDustResponse();
        }
        public ISymbolInfo GetSymbolInfo(string key)
        {
            if (Symbols.ContainsKey(key))
                return Symbols[key];
            else
                return null;
        }
        public IEnumerable<ISymbolInfo> GetSymbols()
        {
            return Symbols.Values.Where(sym => sym.IsTradingEnabled).ToArray();
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

        public void FlushDatabase()
        {
            var allOpenHistories = HistoryDb.ListAvailableData();
            foreach (var hist in allOpenHistories)
                HistoryDb.SaveAndClose(hist, true);
        }

        public void RegisterCustomSerializers(BsonMapper mapper)
        {
            BsonMapperCustom defaultMapper = new BsonMapperCustom();
            var simInfoMapper = defaultMapper.BuildEntityMapper(typeof(BinanceSymbolInfo));
            //---- add mapper for BinanceSymbolInfo 
            mapper.RegisterType<BinanceSymbolInfo>(
                serialize: (obj) => defaultMapper.Serialize<ISymbolInfo>(obj),
                deserialize: (bson) =>
                {
                    var sym = this.GetSymbolInfo(bson["Key"].AsString) as BinanceSymbolInfo;
                    if (sym == null)
                        sym = defaultMapper.Deserialize<BinanceSymbolInfo>(bson);

                    return sym;
                }
            );
            mapper.RegisterType<ISymbolInfo>(
                serialize: (obj) => defaultMapper.Serialize<ISymbolInfo>(obj),
                deserialize: (bson) =>
                {
                    var sym = this.GetSymbolInfo(bson["Key"].AsString) as BinanceSymbolInfo;
                    if (sym == null)
                        sym = defaultMapper.Deserialize<BinanceSymbolInfo>(bson);

                    return sym;
                }
            );

            Order BsonToOrder(BsonValue value)
            {
                lock (LockOrdersTrades)
                {
                    var order = OpenOrders.FirstOrDefault(o => o.Id == value["_id"].AsString);

                    if (order == null)
                        order = Orders.FindById(value["_id"].AsString);
                    bool notFoundIndOrders = order == null;
                    Logger.Debug("Order not found in orders");
                    if (order == null)
                        order = defaultMapper.Deserialize<Order>(value);
                    //if the order was not found in the oders db we add it to openOrders so it will be checked during orders synchronization
                    if (order != null && notFoundIndOrders)
                        OpenOrders.Add(order);

                    return order;
                }
            }

            mapper.RegisterType<IOrder>(
                serialize: (obj) => defaultMapper.Serialize<IOrder>(obj),
                deserialize: BsonToOrder
            );

            Trade DeserializeTrade(BsonValue value)
            {
                Trade result = null;
                lock (LockOrdersTrades)
                    result = Trades.FindById(value["_id"].AsString);
                if (result == null)
                    result = defaultMapper.Deserialize<Trade>(value);
                return result;
            }

            mapper.RegisterType<ITrade>(
                serialize: (obj) => defaultMapper.Serialize<ITrade>(obj),
                deserialize: DeserializeTrade
                );
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
