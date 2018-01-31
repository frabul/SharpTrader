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

namespace SharpTrader
{
    public class BinanceMarketApi : IMarketApi
    {
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

        public string MarketName => "Binance";
        public bool Test { get; set; }
        public DateTime Time => DateTime.UtcNow.AddMilliseconds(ServerTimeDiff);

        public IEnumerable<ISymbolFeed> ActiveFeeds => throw new NotImplementedException();

        public IEnumerable<ITrade> Trades => throw new NotImplementedException();

        public (string Symbol, decimal balance)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();


        public BinanceMarketApi(string apiKey, string apiSecret)
        {
            Client = new BinanceClient(new ApiClient(apiKey, apiSecret));

            ServerTimeSynch();
            //todo, return error
            //Client.TestConnectivity();
            //UserDataStream = Client.StartUserStream().Result;
            //TODO keep alive stream
            ExchangeInfo = Client.GetTradingRulesAsync().Result;
            foreach (var symb in ExchangeInfo.Symbols)
            {
                SymbolsTable.Add(symb.SymbolName, (symb.BaseAsset, symb.QuoteAsset));
            }
            //download account info

            SynchBalance();
            ListenUserData();

            HearthBeatTimer = new System.Timers.Timer()
            {
                Interval = 5000,
                AutoReset = false,
                Enabled = true,

            };
            HearthBeatTimer.Elapsed += HearthBeat;
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

               
                SynchBalance();

                HearthBeatTimer.Start();
            }
        }

        private void SynchBalance()
        {
            if (!BalanceUpdateWatchdog.IsRunning || BalanceUpdateWatchdog.ElapsedMilliseconds > 10000)
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

        Stopwatch LastListenTry = new Stopwatch();

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
                        HandleOrderUpdateMsg,
                        HandleTradeUpdateMsg);
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
            foreach (var bal in msg.Balances)
            {
                this._Balances[bal.Asset] = bal.Free;
            }
        }

        private void HandleOrderUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {

        }

        private void HandleTradeUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {

        }

        public decimal GetBalance(string asset)
        {
            if (_Balances.ContainsKey(asset))
                return _Balances[asset];
            return 0;
        }

        public decimal GetBtcPortfolioValue()
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

        public IMarketOperation LimitOrder(string symbol, TradeType type, decimal amount, double rate)
        {
            throw new NotImplementedException();
        }

        public IMarketOperation MarketOrder(string symbol, TradeType type, decimal amount)
        {
            var side = type == TradeType.Buy ? be.OrderSide.BUY : be.OrderSide.SELL;
            try
            {
                dynamic res;
                if (Test)
                    res = Client.PostNewOrderTest(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                else
                {
                    var ord = Client.PostNewOrder(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                    NewOrders.Add(ord);
                }
                return new MarketOperation(MarketOperationStatus.Completed);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Market operation failed because: " + ex.InnerException.Message);
                return new MarketOperation(MarketOperationStatus.Failed);
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

        class MarketOperation : IMarketOperation
        {
            public MarketOperation(MarketOperationStatus status)
            {
                Status = status;
            }
            public MarketOperationStatus Status { get; internal set; }
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
    }
}
