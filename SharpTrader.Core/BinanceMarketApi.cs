
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using System.Threading;
using System.Diagnostics;
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
using LiteDB;
using System.IO;
using System.Text.RegularExpressions;
using NLog;

using System.Timers;

namespace SharpTrader
{
    public class BinanceMarketApi : IMarketApi
    {
        public event Action<IMarketApi, ITrade> OnNewTrade;

        private Dictionary<string, ApiTrade> TradesPerSymbol = new Dictionary<string, ApiTrade>();
        private LiteCollection<ApiOrder> Orders;
        private LiteCollection<ApiTrade> Trades;
        private List<ApiOrder> OrdersActive = new List<ApiOrder>();
        private object LockOrdersTrades = new object();
        private Stopwatch StopwatchSocketClose = new Stopwatch();
        private Stopwatch UserDataPingStopwatch = new Stopwatch();

        private HistoricalRateDataBase HistoryDb;
        private BinanceClient Client;
        private DisposableBinanceWebSocketClient WSClient;
        private ExchangeInfoResponse ExchangeInfo;
        private NLog.Logger Logger;
        private Dictionary<string, long> UpdatedTrades = new Dictionary<string, long>();

        private Dictionary<string, AssetBalance> _Balances = new Dictionary<string, AssetBalance>();
        private System.Timers.Timer TimerListenUserData;

        private LiteDatabase TradesAndOrdersDb;
        private List<SymbolFeed> Feeds = new List<SymbolFeed>();

        private Guid UserDataSocket;
        private Regex IdRegex = new Regex("([A-Z]+)([0-9]+)", RegexOptions.Compiled);
        private System.Timers.Timer TimerFastUpdates;
        private System.Timers.Timer TimerOrdersTradesSynch;

        public string MarketName => "Binance";

        public DateTime Time => DateTime.UtcNow.Add(Client.TimestampOffset);

        IEnumerable<ITrade> IMarketApi.Trades => Trades.FindAll().ToArray();

        IEnumerable<IOrder> IMarketApi.OpenOrders { get { lock (LockOrdersTrades) return OrdersActive.ToArray(); } }

        public (string Symbol, decimal balance)[] FreeBalances { get { lock (_Balances) return _Balances.Select(kv => (kv.Key, kv.Value.Free)).ToArray(); } }

        public IEnumerable<string> Symbols => ExchangeInfo.Symbols.Select(sym => sym.Symbol);

        public IEnumerable<SymbolInfo> GetSymbols()
        {
            return ExchangeInfo.Symbols.Select(sym => new SymbolInfo
            {
                Asset = sym.BaseAsset,
                QuoteAsset = sym.QuoteAsset,
                Symbol = sym.Symbol,
            });
        }

        public BinanceMarketApi(string apiKey, string apiSecret, HistoricalRateDataBase historyDb, bool resynchTradesAndOrders = false)
        {
            Logger = LogManager.GetLogger("BinanceMarketApi");
            Logger.Info("starting initialization...");
            this.HistoryDb = historyDb;
            var dbPath = $".\\Data\\BinanceAccountsData\\{apiKey}_tnd.db";
            if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            TradesAndOrdersDb = new LiteDatabase(dbPath);

            Orders = TradesAndOrdersDb.GetCollection<ApiOrder>("Orders");
            Orders.EnsureIndex(o => o.Id, true);
            Orders.EnsureIndex(o => o.Symbol);
            Orders.EnsureIndex(o => o.Filled);
            Orders.EnsureIndex(o => o.Status);

            Trades = TradesAndOrdersDb.GetCollection<ApiTrade>("Trades");
            Trades.EnsureIndex(o => o.Id, true);
            Trades.EnsureIndex(o => o.Symbol);
            Trades.EnsureIndex(o => o.OrderId);
            Trades.EnsureIndex(o => o.TradeId);

            Client = new BinanceClient(new ClientConfiguration()
            {
                ApiKey = apiKey,
                SecretKey = apiSecret,
            });


            ExchangeInfo = Client.GetExchangeInfo().Result;
            WSClient = new DisposableBinanceWebSocketClient(Client);
            this.ServerTimeSynch().Wait();
            Logger.Info("synch all operations");
            if (resynchTradesAndOrders)
                SynchAllOperations().Wait();
            //else
            //    SynchTrades();

            Task.WaitAll(ListenUserData(), SynchBalance());
            TimerListenUserData = new System.Timers.Timer(30000)
            {
                AutoReset = false,
                Enabled = true,
            };
            TimerListenUserData.Elapsed += (s, ea) => ListenUserData().ContinueWith(t => TimerListenUserData.Start());

            TimerFastUpdates = new System.Timers.Timer(10000)
            {
                AutoReset = false,
                Enabled = true,
            };
            TimerFastUpdates.Elapsed +=
                (s, e) =>
                {
                    ServerTimeSynch()
                        .ContinueWith(t => SynchBalance())
                        .ContinueWith(t => TimerFastUpdates.Start());
                };

            TimerOrdersTradesSynch = new System.Timers.Timer(120000)
            {
                AutoReset = false,
                Enabled = true,
            };
            TimerOrdersTradesSynch.Elapsed +=
                (s, e) =>
                {
                    Thread.MemoryBarrier();
                    SynchOpenOrders().ContinueWith((t) => SynchTrades()).ContinueWith(t => TimerOrdersTradesSynch.Start());
                };
            Logger.Info("initialization complete");
        }

        public void Dispose()
        {
            Trades = null;
            Orders = null;
            TradesAndOrdersDb.Dispose();
        }

        private async Task SynchAllOperations()
        {
            var symbols = ExchangeInfo.Symbols.Select(s => s.Symbol).ToArray();
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
                    tasks.Add(SyncSymbolTrades(sym));
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

        private async Task SyncSymbolTrades(string sym)
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

        private async Task SynchTrades()
        {
            foreach (var sym in ExchangeInfo.Symbols.Select(s => s.Symbol))
            {
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
                        if (!updatedAlready || lastOrderUpdate != lastOrder.OrderId || hasActiveOrders)
                        {
                            var resp = await Client.GetAccountTrades(new AllTradesRequest { Symbol = sym, Limit = 100 });
                            var trades = resp.Select(tr => new ApiTrade(sym, tr));
                            foreach (var tr in trades)
                            {
                                var order = Orders.FindOne(o => o.OrderId == tr.OrderId && o.Symbol == tr.Symbol);
                                if (order != null)
                                    tr.ClientOrderId = order.ClientId;
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
        }

        private async Task ListenUserData()
        {
            try
            {
                if (UserDataSocket != default(Guid) && (!UserDataPingStopwatch.IsRunning || UserDataPingStopwatch.Elapsed > TimeSpan.FromMinutes(1)))
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
                    catch { UserDataSocket = default(Guid); }
                }

                //every 120 minutes close the socket
                if (!StopwatchSocketClose.IsRunning || StopwatchSocketClose.Elapsed > TimeSpan.FromMinutes(120))
                {
                    StopwatchSocketClose.Restart();
                    CloseUserDataSocket();
                }


                if (UserDataSocket == default(Guid))
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
                if (UserDataSocket != default(Guid))
                {
                    WSClient.CloseWebSocketInstance(UserDataSocket);

                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed closing user data stream because: " + GetExceptionErrorInfo(ex));
            }
            UserDataSocket = default(Guid);
        }

        private void HandleAccountUpdatedMessage(BinanceAccountUpdateData msg)
        {
            lock (_Balances)
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

        private void TradesUpdateOrInsert(ApiTrade tradeUpdate)
        {
            var trade = Trades.FindOne(t => t.Id == tradeUpdate.Id);
            Debug.Assert(tradeUpdate.ClientOrderId != null, "Client order id is null!!");
            if (trade != null)
                Trades.Update(trade);
            else
            {
                Trades.Insert(tradeUpdate);
                OnNewTrade?.Invoke(this, tradeUpdate);
            }
        }

        public async Task<IMarketOperation<IEnumerable<ITrade>>> GetLastTradesAsync(string symbol, int count, string fromId)
        {
            await Task.CompletedTask;
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
                var result = Trades.Find(tr => tr.TradeId >= tradeId && tr.Symbol == symbol);
                return new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new MarketOperation<IEnumerable<ITrade>>(GetExceptionErrorInfo(ex));
            }
        }

        public async Task<IMarketOperation<IEnumerable<ITrade>>> GetAllTradesAsync(string symbol)
        {
            await Task.CompletedTask;
            var result = Trades.Find(tr => tr.Symbol == symbol).ToArray<ITrade>();
            return new MarketOperation<IEnumerable<ITrade>>(MarketOperationStatus.Completed, result);
        }

        public async Task<IMarketOperation<IOrder>> OrderSynchAsync(string id)
        {
            try
            {
                var res = DeconstructId(id);
                var binOrd = await Client.QueryOrder(new QueryOrderRequest() { OrderId = res.id, Symbol = res.symbol });
                var result = new ApiOrder(binOrd);
                OrdersUpdateOrInsert(result);
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

        public async Task<IMarketOperation<ITrade>> GetTradeAsync(string id)
        {
            try
            {
                await Task.CompletedTask;
                var result = Trades.FindOne(o => o.Id == id);
                if (result == null)
                {
                    return new MarketOperation<ITrade>($"Trade {id} not found");
                }
                return new MarketOperation<ITrade>(MarketOperationStatus.Completed, result);
            }
            catch (Exception ex)
            {
                return new MarketOperation<ITrade>(GetExceptionErrorInfo(ex));
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

        public decimal GetFreeBalance(string asset)
        {
            lock (_Balances)
            {
                if (_Balances.ContainsKey(asset))
                    return _Balances[asset].Free;
            }
            return 0;
        }

        public decimal GetEquity(string asset)
        {
            var allPrices = Client.GetSymbolsPriceTicker().Result;
            lock (_Balances)
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

        public async Task<ISymbolFeed> GetSymbolFeedAsync(string symbol)
        {
            SymbolFeed feed;
            lock (Feeds)
                feed = Feeds.Where(sf => sf.Symbol == symbol).FirstOrDefault();
            if (feed == null)
            {
                var symInfo = ExchangeInfo.Symbols.FirstOrDefault(s => s.Symbol == symbol);
                feed = new SymbolFeed(Client, HistoryDb, MarketName, symbol, symInfo.BaseAsset, symInfo.QuoteAsset);
                await feed.Initialize();
                lock (Feeds)
                    Feeds.Add(feed);
            }
            return feed;
        }

        public async Task<IMarketOperation<IOrder>> LimitOrderAsync(string symbol, TradeType type, decimal amount, decimal rate, string clientOrderId = null)
        {
            ResultCreateOrderResponse newOrd;
            var side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell;
            try
            {

                newOrd = (ResultCreateOrderResponse)await Client.CreateOrder(
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

        public async Task<IMarketOperation<IOrder>> MarketOrderAsync(string symbol, TradeType type, decimal amount, string clientOrderId = null)
        {
            var side = type == TradeType.Buy ? OrderSide.Buy : OrderSide.Sell;
            try
            {
                var symbolInfo = ExchangeInfo.Symbols.FirstOrDefault(s => s.Symbol == symbol);
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
                lock (_Balances)
                {
                    newOrd = new ApiOrder(ord);
                    var oldOrd = Orders.FindOne(o => o.Id == newOrd.Id);
                    if (oldOrd != null)
                    {
                        newOrd = oldOrd;
                    }
                    else
                        Orders.Insert(newOrd);

                    var quoteBal = _Balances[symbolInfo.QuoteAsset];
                    var assetBal = _Balances[symbolInfo.BaseAsset];
                    //todo update balance
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, newOrd);
                }
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
                    lock (_Balances)
                    {
                        var symbolInfo = ExchangeInfo.Symbols.FirstOrDefault(s => s.Symbol == order.Symbol);
                        var quoteBal = _Balances[symbolInfo.QuoteAsset];
                        var assetBal = _Balances[symbolInfo.BaseAsset];

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
            private System.Timers.Timer HearthBeatTimer;
            private bool TicksInitialized;

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

            }

            internal async Task Initialize()
            {

                if (Ticks.Count > 0)
                    this.Ask = this.Bid = Ticks.LastTick.Close;

                var book = await Client.GetOrderBook(Symbol, false);
                Ask = (double)book.Asks.First().Price;
                Bid = (double)book.Bids.First().Price;
                HearthBeatTimer = new System.Timers.Timer(2500)
                {
                    AutoReset = false,
                    Enabled = true,
                    Interval = 2500,
                };
                HearthBeatTimer.Elapsed += HearthBeat;

            }

            private void HearthBeat(object state, ElapsedEventArgs args)
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

            private void KlineListen()
            {
                try
                {
                    if (KlineSocket != default(Guid))
                        try { WebSocketClient.CloseWebSocketInstance(KlineSocket); }
                        catch { KlineSocket = default(Guid); }

                    KlineSocket = WebSocketClient.ConnectToKlineWebSocket(this.Symbol.ToLower(), KlineInterval.OneMinute, HandleKlineEvent);
                    KlineWatchdog.Restart();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception during KlineListen: " + GetExceptionErrorInfo(ex));
                }
            }

            private void PartialDepthListen()
            {
                try
                {
                    if (PartialDepthSocket != default(Guid))
                        try { WebSocketClient.CloseWebSocketInstance(PartialDepthSocket); }
                        catch { PartialDepthSocket = default(Guid); }

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

            public override async Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe)
            {
                return await GetNavigatorAsync(timeframe, new DateTime(2016, 1, 1));
            }

            private void HandleKlineEvent(BinanceKlineData msg)
            {
                KlineWatchdog.Restart();
                this.Bid = (double)msg.Kline.Close;

                if (msg.Kline.IsBarFinal && TicksInitialized)
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
                    lock (Locker)
                    {
                        Ticks.AddRecord(candle);
                        UpdateDerivedCharts(candle);
                        SignalTick();
                    }
                }
                RaisePendingEvents(this);
            }


            public async Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe, DateTime historyStartTime)
            {
                if (!TicksInitialized)
                {
                    //load missing data to hist db 
                    Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");

                    var historyToLoad = DateTime.UtcNow - historyStartTime;
                    //--- download latest data
                    var refreshTime = TimeSpan.FromHours(6) < historyToLoad ? TimeSpan.FromHours(6) : historyToLoad;
                    var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb, Client);
                    await downloader.DownloadHistoryAsync(Symbol, historyStartTime, refreshTime); //todo make async

                    //--- load the history into this 
                    ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(this.Market, Symbol, TimeSpan.FromSeconds(60));
                    symbolHistory.Ticks.SeekNearestAfter(DateTime.UtcNow - historyToLoad);
                    lock (Locker)
                    {
                        while (symbolHistory.Ticks.Next())
                            this.Ticks.AddRecord(symbolHistory.Ticks.Tick, true);
                    }
                    TicksInitialized = true;
                }
                return await base.GetNavigatorAsync(timeframe);
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
            public TradeType TradeType { get; set; }
            public OrderType Type { get; set; }
            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
            public List<long> ResultingTrades { get; set; } = new List<long>();

            public ApiOrder() { }

            public ApiOrder(AcknowledgeCreateOrderResponse binanceOrder)
            {
                OrderId = binanceOrder.OrderId;
                ClientId = binanceOrder.ClientOrderId;
                Symbol = binanceOrder.Symbol;
                Market = "Binance";
                Id = Symbol + OrderId;
            }

            public ApiOrder(ResultCreateOrderResponse binanceOrder)
            {
                OrderId = binanceOrder.OrderId;
                ClientId = binanceOrder.ClientOrderId;
                Symbol = binanceOrder.Symbol;
                Market = "Binance";

                TradeType = binanceOrder.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(binanceOrder.Type);
                Amount = binanceOrder.OriginalQuantity;
                Price = binanceOrder.Price;
                Status = GetStatus(binanceOrder.Status);
                Filled = binanceOrder.ExecutedQuantity;
                Id = Symbol + OrderId;
            }

            public ApiOrder(OrderResponse or)
            {
                Symbol = or.Symbol;
                Market = "Binance";
                TradeType = or.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(or.Type);
                Amount = or.OriginalQuantity;
                Price = or.Price;
                Status = GetStatus(or.Status);
                Filled = or.ExecutedQuantity;
                OrderId = or.OrderId;
                Id = Symbol + OrderId;
                ClientId = or.ClientOrderId;
            }

            public ApiOrder(BinanceTradeOrderData bo)
            {
                OrderId = bo.OrderId;
                Symbol = bo.Symbol;
                Market = "Binance";
                TradeType = bo.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Type = GetOrderType(bo.Type);
                Amount = bo.Quantity;
                Price = bo.Price;
                Status = GetStatus(bo.OrderStatus);
                Filled = bo.AccumulatedQuantityOfFilledTradesThisOrder;
                Id = Symbol + OrderId;
                ClientId = bo.OriginalClientOrderId;
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
                Price = tr.Price;
                Amount = tr.Quantity;
                Fee = tr.Commission;
                FeeAsset = tr.CommissionAsset;
                Time = tr.Time;
                OrderId = tr.OrderId;
                TradeId = tr.Id;
                Id = Symbol + TradeId;
            }

            public ApiTrade(BinanceTradeOrderData tr)
            {
                Market = "Binance";
                Symbol = tr.Symbol;
                Type = tr.Side == OrderSide.Buy ? TradeType.Buy : TradeType.Sell;
                Price = tr.PriceOfLastFilledTrade;
                Amount = tr.QuantityOfLastFilledTrade;
                Fee = Fee;
                FeeAsset = FeeAsset;
                Time = tr.TimeStamp;
                OrderId = tr.OrderId;
                ClientOrderId = tr.OriginalClientOrderId;
                TradeId = tr.TradeId;
                Id = Symbol + TradeId;
            }
            [BsonId]
            public string Id { get; set; }
            public long TradeId { get; set; }
            public long OrderId { get; set; }
            public string ClientOrderId { get; set; }
            public decimal Amount { get; set; }
            public decimal Fee { get; set; }
            public string Market { get; set; }
            public decimal Price { get; set; }
            public string Symbol { get; set; }
            public TradeType Type { get; set; }
            public string FeeAsset { get; set; }
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
}
