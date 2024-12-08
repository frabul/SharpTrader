using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.Response.Error;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader.Core.BrokersApi.Binance
{
    public class BinanceTradeBarsRepository : TradeBarsRepository
    {
        NLog.Logger Logger = NLog.LogManager.GetLogger("BinanceTradeBarsRepository");
        private BinanceClient Client;
        private Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
        private Dictionary<string, DateTime> LastHistoryRequest = new Dictionary<string, DateTime>();
        private readonly string MarketName = "Binance";
        private SemaphoreSlim DownloadCandlesSemaphore;
        public int ConcurrencyCount { get; set; } = 10;

        public BinanceTradeBarsRepository(string dataDir, BinanceClient cli) : base(dataDir)
        {
            Client = cli;
            _ = CheckClosing();
            DownloadCandlesSemaphore = new SemaphoreSlim(ConcurrencyCount, ConcurrencyCount);
        }


        //--------------------------------------------
        public BinanceTradeBarsRepository(string dataDir, double rateLimitFactor = 0.4f) : base(dataDir)
        {
            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });
            DownloadCandlesSemaphore = new SemaphoreSlim(ConcurrencyCount, ConcurrencyCount);
        }

        private async Task CheckClosing()
        {
            while (true)
            {
                lock (LastHistoryRequest)
                {
                    foreach (var kv in LastHistoryRequest.ToArray())
                    {
                        var key = kv.Key;
                        var lastReq = kv.Value;
                        if (DateTime.Now > lastReq.AddMinutes(5))
                        {
                            this.SaveAndClose(SymbolHistoryId.Parse(key), true);
                            LastHistoryRequest.Remove(key);
                        }
                    }
                }
                await Task.Delay(10000);
            }
        }

        public async Task SynchSymbolsTableAsync(string DataDir)
        {
            Dictionary<string, SymbolInfo> dict = new Dictionary<string, SymbolInfo>();
            var tradingRules = await Client.GetExchangeInfo();
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.symbol,
                    new SymbolInfo
                    {
                        Asset = symb.baseAsset,
                        QuoteAsset = symb.quoteAsset,
                        Key = symb.symbol,
                        IsMarginTadingAllowed = symb.isMarginTradingAllowed,
                        IsSpotTadingAllowed = symb.isSpotTradingAllowed,
                        IsBorrowAllowed = symb.isMarginTradingAllowed
                    });

            }
            var crossPairs = await Client.GetAllCrossMarginPairs();
            foreach (var pair in crossPairs)
            {
                if (dict.ContainsKey(pair.symbol))
                    dict[pair.symbol].IsCrossMarginAllowed = true;
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, SymbolInfo>>(json);
            System.IO.File.WriteAllText(System.IO.Path.Combine(DataDir, "BinanceSymbolsTable.json"), json);
        }

        public async Task AssureData(SymbolHistoryId histInfo, DateTime fromTime, DateTime toTime)
        {
            if (toTime > DateTime.Now.AddYears(10))
                toTime = DateTime.Now.AddYears(10);
            toTime = new DateTime(toTime.Year, toTime.Month, toTime.Day, toTime.Hour, toTime.Minute, 0, DateTimeKind.Utc);

            var epoch = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
            if (fromTime < epoch)
                fromTime = epoch;

            await DownloadCandlesSemaphore.WaitAsync();
            var sem = GetSemaphore(histInfo.Symbol);
            try
            {
                await sem.WaitAsync();
                var hist = this.GetSymbolHistory(histInfo, fromTime, toTime);
                var oldTicks = hist.Ticks;

                //first find next available data, if not found download everything 
                DateTime checkTime = fromTime;
                while (checkTime < toTime)
                {
                    if (oldTicks.MoveNext())
                    {
                        if (oldTicks.Time - checkTime > histInfo.Resolution)
                        {
                            Logger.Debug($"Hole found in {histInfo.Symbol} history from {checkTime} to {oldTicks.Time}.");
                            var candles = await DownloadCandles(histInfo.Symbol, checkTime, oldTicks.Current.CloseTime);
                            this.AddCandlesticks(histInfo, candles);
                        }
                        checkTime = oldTicks.Current.CloseTime;
                    }
                    else
                    {
                        if (oldTicks.Count < 1 || oldTicks.Current.CloseTime < checkTime)
                        {
                            Logger.Debug($"Hole found in {histInfo.Symbol} history from {checkTime} to {toTime}.");
                            //there isn't any other tick, all remaining data needs to be downloaded 
                            var candles = await DownloadCandles(histInfo.Symbol, checkTime, toTime);
                            this.AddCandlesticks(histInfo, candles);
                            checkTime = toTime;
                        }
                        else
                            checkTime = checkTime + histInfo.Resolution;


                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Error("Fatal Exception while download history for symbol {0}: {1}", histInfo.Symbol, ex.Message);
            }
            finally
            {
                DownloadCandlesSemaphore.Release();
                sem.Release();
            }
            //this.SaveAll();
        }

        private async Task<List<Candlestick>> DownloadCandles(string symbol, DateTime startTime, DateTime endTime)
        {
            Logger.Info($"Downloading candles for {symbol} from {startTime} to {endTime}");
            List<Candlestick> allCandles = new List<SharpTrader.Candlestick>();
            try
            {
                bool noMoreData = false;
                int zeroCount = 0;

                while (!noMoreData && (allCandles.Count < 1 || allCandles.Last().CloseTime < endTime))
                {
                    //Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                    try
                    {
                        var candles = await Client.GetKlinesCandlesticks(new GetKlinesCandlesticksRequest
                        {
                            Symbol = symbol,
                            StartTime = startTime - TimeSpan.FromSeconds(60),
                            Interval = KlineInterval.OneMinute,
                            EndTime = endTime - TimeSpan.FromSeconds(60),
                        });

                        var batch = candles.Select(KlineToCandlestick).ToList();

                        allCandles.AddRange(batch);

                        //if we get no data for more than 3 requests then we can assume that there isn't any more data
                        if (batch.Count < 1)
                            zeroCount++;
                        else
                            zeroCount = 0;
                        if (zeroCount > 1)
                            noMoreData = true;
                    }
                    catch (Exception ex)
                    {
                        var msg = $"Exception during {symbol} history download: ";
                        if (ex is BinanceException binException)
                            msg += binException.ErrorDetails;
                        else
                            msg += ex.Message;
                        Console.WriteLine(msg);
                        await Task.Delay(3000);
                    }
                    if (allCandles.Count > 1)
                        startTime = new DateTime((allCandles[allCandles.Count - 1].CloseTime - TimeSpan.FromSeconds(1)).Ticks, DateTimeKind.Utc);
                }
            }
            catch
            {

            }
            finally
            {
            }


            return allCandles;
        }

        private static Candlestick KlineToCandlestick(KlineCandleStickResponse c)
        {
            return new SharpTrader.Candlestick()
            {
                Open = (double)c.Open,
                High = (double)c.High,
                Low = (double)c.Low,
                Close = (double)c.Close,
                OpenTime = c.OpenTime,
                CloseTime = c.OpenTime.AddSeconds(60), //+ c.CloseTime.AddMilliseconds(1),
                QuoteAssetVolume = (double)c.QuoteAssetVolume
            };

        }

        private SemaphoreSlim GetSemaphore(string symbol)
        {
            if (!Semaphores.ContainsKey(symbol))
                Semaphores.Add(symbol, new SemaphoreSlim(1, 1));
            return Semaphores[symbol];
        }

        public override ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData, DateTime endOfData)
        {
            lock (LastHistoryRequest)
                LastHistoryRequest[info.Key] = DateTime.Now;
            return base.GetSymbolHistory(info, startOfData, endOfData);
        }

        public void DownloadSymbols(Func<ExchangeInfoSymbol, bool> filter, TimeSpan redownloadSpan)
        {
            var exchangeInfo = Client.GetExchangeInfo().Result;
            var symbols = exchangeInfo.Symbols;

            var toDownload = symbols
                .Where(filter)
                .Select(sp => sp.symbol).ToList();
            var downloadQueue = new Queue<string>(toDownload);
            List<Task> tasks = new List<Task>();
            Stopwatch swReportRate = new Stopwatch();
            swReportRate.Start();
            while (downloadQueue.Count > 0)
            {
                if (tasks.Count < ConcurrencyCount)
                {
                    var symbol = downloadQueue.Dequeue();
                    var task = DownloadHistoryAsync(symbol, DateTime.UtcNow.Subtract(TimeSpan.FromDays(360)), redownloadSpan);
                    tasks.Add(task);

                }
                foreach (var t in tasks.Where(t => t.IsCompleted).ToArray())
                {
                    tasks.Remove(t);
                    if (t.Exception != null)
                        Console.WriteLine("A task terminated with an exception");
                }


                System.Threading.Thread.Sleep(1);
            }
            while (tasks.Any(t => !t.IsCompleted))
                System.Threading.Thread.Sleep(1);
        }

        public async Task AssureFilter(Func<string, bool> filter, DateTime fromTime, DateTime toTime)
        {
            var exchangeInfo = Client.GetExchangeInfo().Result;
            var symbols = exchangeInfo.Symbols;

            var toDownload = symbols

                .Where(s => filter(s.symbol))
                .Select(sp => sp.symbol).ToList();
            toDownload.Sort();
            List<Task> tasks = new List<Task>();
            foreach (var sym in toDownload)
            {
                var histInfo = new SymbolHistoryId("Binance", sym, TimeSpan.FromMinutes(1));
                var task = this.AssureData(histInfo, fromTime, toTime).ContinueWith(t => this.SaveAndClose(histInfo, true));
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }

        public async Task DownloadHistoryAsync(string symbol, DateTime fromTime, TimeSpan redownloadStart)
        {
            //we must assure that there is only one downloading action ongoing for each symbol!
            var semaphore = GetSemaphore(symbol);
            try
            {
                await semaphore.WaitAsync();
                Console.WriteLine($"Downloading {symbol} history ");
                DateTime endTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));

                //we need to convert all in UTC time 
                var startTime = fromTime; //  
                var epoch = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
                if (startTime < epoch)
                    startTime = epoch;

                //try to get the first candle - use these functions for performance improvement
                var symId = new SymbolHistoryId(MarketName, symbol, TimeSpan.FromSeconds(60));
                var metaData = this.GetMetaData(symId);

                if (metaData.FirstBar != null && metaData.LastBar != null)
                {

                    if (startTime > metaData.FirstBar.OpenTime)
                    {
                        //if startTime is after the first recorded bar then 
                        // then we can start downloading from last recorded bar  
                        if (endTime <= metaData.LastBar.Time)
                            endTime = startTime;
                        startTime = new DateTime(metaData.LastBar.OpenTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);

                    }
                    else
                    {
                        if (metaData.FirstKnownData == null)
                        {
                            //start time is earlier than first known candle.. we start download from first candle on server
                            var firstAvailable = (await Client.GetKlinesCandlesticks(
                                new GetKlinesCandlesticksRequest
                                {
                                    Symbol = symbol,
                                    StartTime = startTime,
                                    Interval = KlineInterval.OneMinute,
                                    EndTime = startTime.AddYears(3),
                                    Limit = 10
                                })).FirstOrDefault();
                            this.UpdateFirstKnownData(metaData.HistoryId, KlineToCandlestick(firstAvailable));
                        }

                        //if we already downloaded the first available we can start download from the last known
                        //otherwise we leave start time as it is to avoid creating holes 
                        if (metaData.FirstKnownData.CloseTime.AddSeconds(1) >= metaData.FirstBar.OpenTime)
                        {
                            startTime = new DateTime(metaData.LastBar.Time.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);
                        }

                        // avoid downloading again some data if possible
                        if (endTime <= metaData.LastBar.Time)
                            endTime = metaData.FirstBar.Time;
                    }
                }

                //download and add bars if needed
                if (endTime > startTime)
                {
                    var candlesDownloaded = await DownloadCandles(symbol, startTime, endTime);
                    //---
                    this.AddCandlesticks(symId, candlesDownloaded);
                    this.ValidateData(symId);
                    this.SaveAndClose(symId);
                }

                Console.WriteLine($"{symbol} history downloaded");
            }
            catch (Exception ex)
            {
                var msg = $"Fatal Exception during {symbol} history download: ";
                if (ex is BinanceException binException)
                    msg += binException.ErrorDetails;
                else
                    msg += ex.Message;
                Console.WriteLine(msg);
            }
            finally
            {
                semaphore.Release();
            }
        }

    }
}
