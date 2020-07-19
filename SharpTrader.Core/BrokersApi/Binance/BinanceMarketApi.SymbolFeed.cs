
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SharpTrader.BrokersApi.Binance
{
    class SymbolFeed : ISymbolFeed, IDisposable
    {
        public event Action<ISymbolFeed, IBaseData> OnData;
        private NLog.Logger Logger;
        private BinanceClient Client;
        private CombinedWebSocketClient WebSocketClient;
        private HistoricalRateDataBase HistoryDb;
        private Task HearthBeatTask;
        private Stopwatch KlineWatchdog = new Stopwatch();
        private Stopwatch DepthWatchdog = new Stopwatch();
        private DateTime LastKlineWarn = DateTime.Now;
        private DateTime LastDepthWarn = DateTime.Now;
        private BinanceKline FormingCandle = new BinanceKline() { StartTime = DateTime.MaxValue };
        private static Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
        private volatile bool HistoryInitialized = false;
        private HistoryInfo HistoryInfo;

        ISymbolInfo ISymbolFeed.Symbol => Symbol;
        public SymbolInfo Symbol { get; private set; }
        public DateTime Time { get; private set; }
        public double Ask { get; private set; }
        public double Bid { get; private set; }
        public string Market { get; private set; }
        public double Spread { get; set; }
        public double Volume24H { get; private set; }
        public bool Disposed { get; private set; }

        public SymbolFeed(BinanceClient client, CombinedWebSocketClient websocket, HistoricalRateDataBase hist, string market, SymbolInfo symbol, DateTime timeNow)
        {
            HistoryDb = hist;
            this.Client = client;
            this.WebSocketClient = websocket;
            this.Symbol = symbol;
            this.Market = market;
            this.Time = timeNow;
            Logger = LogManager.GetLogger("Bin" + Symbol + "Feed");
        }

        internal async Task Initialize()
        {
            var book = await Client.GetOrderBook(Symbol.Key, false);
            Ask = (double)book.Asks.First().Price;
            Bid = (double)book.Bids.First().Price;
            KlineListen();
            PartialDepthListen();
            DepthWatchdog.Restart();
            KlineWatchdog.Restart();
            HearthBeatTask = HearthBeat();
        }

        private async Task HearthBeat()
        {
            while (!Disposed)
            {
                try
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

                }
                catch
                {

                }
                await Task.Delay(2500);
            }

        }

        private void KlineListen()
        {
            try
            {
                WebSocketClient.SubscribeKlineStream(this.Symbol.Key.ToLower(), KlineInterval.OneMinute, HandleKlineEvent);
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during KlineListen: " + BinanceMarketApi.GetExceptionErrorInfo(ex));
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
                Logger.Error("Exception during PartialDepthListen: " + BinanceMarketApi.GetExceptionErrorInfo(ex));
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
                //this.OnData?.Invoke(this, new QuoteTick(Bid, Ask, messageData.EventTime));
            }
            this.Time = messageData.EventTime;
        }

        private void HandleKlineEvent(BinanceKlineData msg)
        {
            this.Time = msg.EventTime;
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
                    QuoteAssetVolume = (double)FormingCandle.QuoteVolume
                };
                if (HistoryInitialized)
                    HistoryDb.AddCandlesticks("Binance", Symbol.Key, new[] { candle });
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
                    QuoteAssetVolume = (double)msg.Kline.QuoteVolume
                };
                if (HistoryInitialized)
                    HistoryDb.AddCandlesticks("Binance", Symbol.Key, new[] { candle });
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

            if (historyStartTime > this.Time)
                throw new InvalidOperationException("Requested future quotes");
            if (resolution != TimeSpan.FromMinutes(1))
                throw new NotSupportedException("Binance symbol history only supports resolution 1 minute");

            var history = new TimeSerie<ITradeBar>();
            var sem = GetSemaphore();
            await sem.WaitAsync();
            try
            {
                //download missing data to hist db   
                //todo move this part to history db
                if (!HistoryInitialized)
                {
                    Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");
                    var downloader = new BinanceDataDownloader(HistoryDb, Client);
                    await downloader.DownloadHistoryAsync(Symbol.Key, historyStartTime, TimeSpan.FromHours(6));
                }

                //--- get the data from db
                HistoryInfo = new HistoryInfo(this.Market, Symbol.Key, TimeSpan.FromSeconds(60));
                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(HistoryInfo, historyStartTime, DateTime.MaxValue);
                
                //HistoryDb.CloseFile(this.Market, Symbol.Key, TimeSpan.FromSeconds(60));
                
                while (symbolHistory.Ticks.MoveNext())
                    history.AddRecord(symbolHistory.Ticks.Current, true);
                HistoryInitialized = true;
            }
            catch(Exception ex)
            {
                Logger.Error("Exception during SymbolFeed.GetHistoryNavigator: {0}", ex.Message);
            }
            finally
            {
                sem.Release();
            }
            return history;
        }

        public void Dispose()
        {
            HistoryDb.CloseFile(HistoryInfo);
            this.Disposed = true;
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


        private SemaphoreSlim GetSemaphore()
        {
            if (!Semaphores.ContainsKey(this.Symbol.Key))
                Semaphores.Add(this.Symbol, new SemaphoreSlim(1, 1));
            return Semaphores[this.Symbol];
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
