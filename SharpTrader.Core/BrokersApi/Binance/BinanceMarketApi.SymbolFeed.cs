
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Collections.Concurrent;

namespace SharpTrader.BrokersApi.Binance
{
    class SymbolFeed : ISymbolFeed, IDisposable
    {
        public event Action<ISymbolFeed, IBaseData> OnData;
        private Serilog.ILogger Logger;
        private BinanceClient Client;
        private CombinedWebSocketClient WebSocketClient;
        private BinanceTradeBarsRepository HistoryDb;
        private Task HearthBeatTask;
        private Stopwatch KlineWatchdog = new Stopwatch();
        private Stopwatch DepthWatchdog = new Stopwatch();
        private DateTime LastKlineWarn = DateTime.Now;
        private DateTime LastDepthWarn = DateTime.Now;

        private static Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
        private SymbolHistoryId HistoryId;

        private ConcurrentQueue<BinanceKlineData> KlinesReceived = new ConcurrentQueue<BinanceKlineData>();
        private ConcurrentQueue<BinancePartialData> PartialDepthsReceived = new ConcurrentQueue<BinancePartialData>();

        private Candlestick FormingCandle = new Candlestick();
        private Candlestick LastEmittedCandled = new Candlestick();

        ISymbolInfo ISymbolFeed.Symbol => Symbol;
        public BinanceSymbolInfo Symbol { get; private set; }
        public DateTime Time { get; private set; }
        public double Ask { get; private set; }
        public double Bid { get; private set; }
        public string Market { get; private set; }
        public double Spread { get; set; }
        public double Volume24H { get; private set; }
        public bool Disposed { get; private set; }
        internal volatile int Users = 0;

        public SymbolFeed(BinanceClient client, CombinedWebSocketClient websocket, BinanceTradeBarsRepository hist, string market, BinanceSymbolInfo symbol, DateTime timeNow)
        {
            HistoryDb = hist;
            this.Client = client;
            this.WebSocketClient = websocket;
            this.Symbol = symbol;
            this.Market = market;
            this.Time = timeNow;
            Logger = Serilog.Log
                .ForContext<SymbolFeed>()
                .ForContext("Symbol", symbol.ToString());

            HistoryId = new SymbolHistoryId(this.Market, Symbol.Key, TimeSpan.FromSeconds(60));

        }

        internal async Task Initialize()
        {
            var book = await Client.GetOrderBook(Symbol.Key, false);

            if (book.Asks.Count > 0)
                Ask = (double)book.Asks.FirstOrDefault().Price;
            else
                Ask = float.MaxValue;
            if (book.Bids.Count > 0)
                Bid = (double)book.Bids.FirstOrDefault().Price;
            else
                Bid = 0;
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
                    if (KlineWatchdog.Elapsed > TimeSpan.FromSeconds(120))
                    {
                        KlineWatchdog.Restart();
                        if (DateTime.Now > LastKlineWarn.AddSeconds(900))
                        {
                            Logger.Warning("{Symbol} Kline websock looked like frozen", Symbol);
                            LastKlineWarn = DateTime.Now;
                        }
                        KlineListen();
                    }
                    if (DepthWatchdog.Elapsed > TimeSpan.FromSeconds(240))
                    {
                        DepthWatchdog.Restart();
                        if (DateTime.Now > LastDepthWarn.AddSeconds(90))
                        {
                            Logger.Warning("{Symbol} Depth websock looked like frozen", Symbol);
                            LastDepthWarn = DateTime.Now;
                        }
                        PartialDepthListen();
                    }
                }
                catch
                {

                }

                DigestPartialDepthMessageQueue();
                DigestKlineMessageQueue();
                await Task.Delay(50);
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
                Logger.Error(ex, "Exception during KlineListen: {Message}", BinanceMarketApi.GetExceptionErrorInfo(ex));
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
                Logger.Error(ex, "Exception during PartialDepthListen: {Message}", BinanceMarketApi.GetExceptionErrorInfo(ex));
            }

        }

        private void HandlePartialDepthUpdate(BinancePartialData messageData)
        {
            DepthWatchdog.Restart();
            PartialDepthsReceived.Enqueue(messageData);
        }

        private void HandleKlineEvent(BinanceKlineData msg)
        {
            KlineWatchdog.Restart();
            KlinesReceived.Enqueue(msg);
        }

        private void DigestPartialDepthMessageQueue()
        {
            while (PartialDepthsReceived.TryDequeue(out BinancePartialData messageData))
            {
                var bid = messageData.Bids.FirstOrDefault(b => b.Quantity > 0);
                var ask = messageData.Asks.FirstOrDefault(a => a.Quantity > 0);

                if (ask != null)
                    Ask = (double)ask.Price;
                else
                    Ask = float.MaxValue;
                if (bid != null)
                    Bid = (double)bid.Price;
                else
                    Bid = 0;
                Spread = Ask - Bid;

                this.Time = messageData.EventTime;
            }
        }

        private void DigestKlineMessageQueue()
        {
            bool is_different(decimal a, double b) => (a - (decimal)b) / a > 0.0001m;
            decimal diff(decimal a, double b) => (a - (decimal)b) / a;
            TimeSpan resolution = TimeSpan.FromMinutes(1);
            List<Candlestick> CandlesToAdd = new List<Candlestick>(10);
            while (KlinesReceived.TryDequeue(out BinanceKlineData msg))
            {
                //we need to check that the received candle is not preceding the last emitted
                if (msg.Kline.StartTime <= LastEmittedCandled.OpenTime)
                {
                    if (is_different(msg.Kline.Close, LastEmittedCandled.Close))
                        Logger.Warning("{Symbol} SymbolFeed: received candle {@NewCandle} precending last one {@PreviousCandle} - diff {Diff} ",
                            Symbol.Key,
                            msg.Kline.StartTime,
                            LastEmittedCandled.OpenTime,
                            diff(msg.Kline.Close, LastEmittedCandled.Close)
                            );
                    continue;
                }

                this.Time = msg.EventTime;
                var candleReceived = KlineToCandlestick(msg.Kline);

                //trace final bar  
                if (msg.Kline.IsBarFinal)
                    Logger.Verbose("{Symbol} - Final bar {@Candle} arrived at local system time {Time:HH.mm.ss} ",
                        msg.Symbol,
                        msg.Kline,
                        DateTime.UtcNow);


                FormingCandle = candleReceived;

                //emit candle if it is final
                if (msg.Kline.IsBarFinal)
                {

                    LastEmittedCandled = candleReceived;
                    CandlesToAdd.Add(LastEmittedCandled);
                    //the new forming candle is a filler
                    FormingCandle = new Candlestick(
                                LastEmittedCandled.CloseTime,
                                LastEmittedCandled.CloseTime + resolution,
                                LastEmittedCandled.Close,
                                LastEmittedCandled.Close,
                                LastEmittedCandled.Close,
                                LastEmittedCandled.Close,
                                0
                            );
                }

            }
            //instad of waiting for the candle we just emit forming candle  
            if (!LastEmittedCandled.IsDefault())
                if (DateTime.UtcNow > LastEmittedCandled.Time.AddSeconds(3.5) + resolution)
                {
                    Logger.Verbose("{Symbol} SymbolFeed: emitting forming candle ", Symbol.Key);
                    this.Time = DateTime.UtcNow;
                    LastEmittedCandled = FormingCandle;
                    CandlesToAdd.Add(LastEmittedCandled);
                    FormingCandle = new Candlestick(
                                    LastEmittedCandled.CloseTime,
                                    LastEmittedCandled.CloseTime + resolution,
                                    LastEmittedCandled.Close,
                                    LastEmittedCandled.Close,
                                    LastEmittedCandled.Close,
                                    LastEmittedCandled.Close,
                                    0
                                );
                }
            if (CandlesToAdd.Count > 0)
                HistoryDb.AddCandlesticks(HistoryId, CandlesToAdd);
            foreach (var c in CandlesToAdd)
            {
                this.OnData?.Invoke(this, c);
            }
        }

        private static Candlestick KlineToCandlestick(BinanceKline kline)
        {
            return new Candlestick()
            {
                Close = (double)kline.Close,
                High = (double)kline.High,
                CloseTime = kline.StartTime.AddSeconds(60),
                OpenTime = kline.StartTime,
                Low = (double)kline.Low,
                Open = (double)kline.Open,
                QuoteAssetVolume = (double)kline.QuoteVolume
            };
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
                //--- get the data from db
                await HistoryDb.AssureData(HistoryId, historyStartTime, this.Time);
                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(HistoryId, historyStartTime, DateTime.MaxValue);

                //HistoryDb.CloseFile(this.Market, Symbol.Key, TimeSpan.FromSeconds(60)); 
                while (symbolHistory.Ticks.MoveNext())
                {
                    try { history.AddRecord(symbolHistory.Ticks.Current, true); }
                    catch
                    {
                        Logger.Warning("Bad candle {@Candle} in history {@HistoryId}", symbolHistory.Ticks.Current, HistoryId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception during SymbolFeed.GetHistoryNavigator: {Message}", ex.Message);
            }
            finally
            {
                sem.Release();
            }
            return history;
        }

        public void Dispose()
        {
            HistoryDb.SaveAndClose(HistoryId, false);
            this.Disposed = true;
            try
            {
                WebSocketClient.Unsubscribe<BinanceKlineData>(HandleKlineEvent);
                WebSocketClient.Unsubscribe<BinancePartialData>(HandlePartialDepthUpdate);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exeption during SymbolFeed.Dispose: {Message}", ex.Message);
            }
        }


        private SemaphoreSlim GetSemaphore()
        {
            lock (Semaphores)
            {
                if (!Semaphores.ContainsKey(this.Symbol.Key))
                    Semaphores.Add(this.Symbol, new SemaphoreSlim(1, 1));
                return Semaphores[this.Symbol];
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
            //round price to it's maximum precision
            price = NearestRoundHigher(price, this.Symbol.PricePrecision);

            if (amount * price < Symbol.MinNotional)
                amount = Symbol.MinNotional / price;

            if (amount < Symbol.MinLotSize)
                amount = Symbol.MinLotSize;

            amount = NearestRoundHigher(amount, Symbol.LotSizeStep);

            //check min notional ( abort if not met )
            if (amount * price < Symbol.MinNotional)
                amount = 0;

            return (price / 1.00000000000m, amount / 1.000000000000m);
        }

        public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal amount, decimal price)
        {
            //round price to it's maximum precision
            price = NearestRoundLower(price, this.Symbol.PricePrecision);
            //if amount is lower than min lot size then abort
            //else round amount
            if (amount < Symbol.MinLotSize)
                amount = 0;
            else
                amount = NearestRoundLower(amount, Symbol.LotSizeStep);

            //check min notional ( abort if not met )
            if (amount * price < Symbol.MinNotional)
                amount = 0;
            return (price / 1.00000000000m, amount / 1.000000000000m);
        }
    }
}
