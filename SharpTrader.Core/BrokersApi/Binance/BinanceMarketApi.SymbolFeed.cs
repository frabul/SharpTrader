
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using NLog;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Storage;
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
        private BinanceTradeBarsRepository HistoryDb;
        private Task HearthBeatTask;
        private Stopwatch KlineWatchdog = new Stopwatch();
        private Stopwatch DepthWatchdog = new Stopwatch();
        private DateTime LastKlineWarn = DateTime.Now;
        private DateTime LastDepthWarn = DateTime.Now;
        private BinanceKline FormingCandle = new BinanceKline() { StartTime = DateTime.MaxValue };
        private static Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
        private SymbolHistoryId HistoryId;
        private Candlestick LastFullCandle;

        ISymbolInfo ISymbolFeed.Symbol => Symbol;
        public SymbolInfo Symbol { get; private set; }
        public DateTime Time { get; private set; }
        public double Ask { get; private set; }
        public double Bid { get; private set; }
        public string Market { get; private set; }
        public double Spread { get; set; }
        public double Volume24H { get; private set; }
        public bool Disposed { get; private set; }

        public SymbolFeed(BinanceClient client, CombinedWebSocketClient websocket, BinanceTradeBarsRepository hist, string market, SymbolInfo symbol, DateTime timeNow)
        {
            HistoryDb = hist;
            this.Client = client;
            this.WebSocketClient = websocket;
            this.Symbol = symbol;
            this.Market = market;
            this.Time = timeNow;
            Logger = LogManager.GetLogger("Bin" + Symbol + "Feed");

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
                            Logger.Warn("{0} Kline websock looked like frozen", Symbol);
                            LastKlineWarn = DateTime.Now;
                        }
                        KlineListen();
                    }
                    if (DepthWatchdog.Elapsed > TimeSpan.FromSeconds(120))
                    {
                        DepthWatchdog.Restart();
                        if (DateTime.Now > LastDepthWarn.AddSeconds(90))
                        {
                            Logger.Warn("{0}Depth websock looked like frozen", Symbol);
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

        private void HandleKlineEvent(BinanceKlineData msg)
        {
            KlineWatchdog.Restart();
            TimeSpan resolution = TimeSpan.FromMinutes(1);
            this.Time = msg.EventTime;
            List<Candlestick> CandlesToAdd = new List<Candlestick>(10);
            var kline = msg.Kline;

            //check if there could be a gap
            if (LastFullCandle != null && FormingCandle != null && kline.StartTime > FormingCandle.StartTime)
            {
                if (FormingCandle.StartTime > LastFullCandle.OpenTime)
                {
                    //there is a gap
                    var forming = KlineToCandlestick(FormingCandle);
                    CandlesToAdd.Add(forming);
                    DateTime time = FormingCandle.StartTime + resolution;
                    while (time < kline.StartTime)
                    {
                        var filler = new Candlestick(forming);
                        filler.Open = filler.Close;
                        filler.High = filler.Close;
                        filler.Low = filler.Close;
                        CandlesToAdd.Add(filler);
                    }
                    Logger.Warn("{0:HH.mm.ss} - {1} symbol feed - found gap in kline events, {2} missing\n arrived now {3:HH.mm.ss} - forming {4:HH.mm.ss}",
                        time,
                        Symbol.Key,
                        CandlesToAdd.Count,
                        kline.StartTime, forming.OpenTime);
                }
            }

            if (msg.Kline.IsBarFinal)
            {
                LastFullCandle = KlineToCandlestick(msg.Kline);
                this.OnData?.Invoke(this, LastFullCandle);
                CandlesToAdd.Add(LastFullCandle);
            }
            if (CandlesToAdd.Count > 0)
                HistoryDb.AddCandlesticks(HistoryId, CandlesToAdd);
            foreach (var c in CandlesToAdd)
            {
                LastFullCandle = c;
                this.OnData?.Invoke(this, c);
            }


            FormingCandle = msg.Kline;
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
                        Logger.Warn("Bad candle {0} in history {1}", symbolHistory.Ticks.Current.Time, symbolHistory.Symbol);
                    }
                }
            }
            catch (Exception ex)
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
            HistoryDb.SaveAndClose(HistoryId, false);
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
