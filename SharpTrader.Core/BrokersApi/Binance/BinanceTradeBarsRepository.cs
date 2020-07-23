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
        NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private BinanceClient Client;
        private Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
        private readonly string MarketName = "Binance";
        private SemaphoreSlim DownloadCandlesSemaphore = new SemaphoreSlim(1, 1);

        public BinanceTradeBarsRepository(string dataDir, BinanceClient cli) : base(dataDir)
        {
            Client = cli;
        }

        //--------------------------------------------
        public BinanceTradeBarsRepository(string dataDir, double rateLimitFactor = 0.6f) : base(dataDir)
        {
            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });
        }
         
        public void SynchSymbolsTable(string DataDir)
        {
            Dictionary<string, SymbolInfo> dict = new Dictionary<string, SymbolInfo>();
            var tradingRules = Client.GetExchangeInfo().Result;
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.symbol,
                    new SymbolInfo
                    {
                        Asset = symb.baseAsset,
                        QuoteAsset = symb.quoteAsset,
                        Key = symb.symbol,
                        IsMarginTadingAllowed = symb.isMarginTradingAllowed,
                        IsSpotTadingAllowed = symb.isSpotTradingAllowed
                    });

            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, SymbolInfo>>(json);
            System.IO.File.WriteAllText(DataDir + "BinanceSymbolsTable.json", json);
        }

        public async Task AssureData(SymbolHistoryId histInfo, DateTime fromTime, DateTime toTime)
        {
            if (toTime > DateTime.Now.AddYears(5))
                toTime = DateTime.Now.AddYears(5);

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

                DateTime lastTime = fromTime;
                while (lastTime < toTime)
                {
                    if (oldTicks.MoveNext())
                    {
                        if (oldTicks.Time - lastTime > histInfo.Timeframe)
                        {
                            Logger.Debug($"Hole found in {histInfo.Symbol} history from {lastTime} to {hist.Ticks.NextTickTime}.");
                            var candles = await DownloadCandles(histInfo.Symbol, lastTime, oldTicks.Current.CloseTime);
                            this.AddCandlesticks(histInfo, candles);
                        }
                        lastTime = oldTicks.Current.CloseTime;
                    }
                    else
                    {
                        Logger.Debug($"Hole found in {histInfo.Symbol} history from {lastTime} to {toTime}.");
                        //there isn't any other tick, all remaining data needs to be downloaded 
                        var candles = await DownloadCandles(histInfo.Symbol, lastTime, toTime);
                        this.AddCandlesticks(histInfo, candles);
                        lastTime = toTime;
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
        }

      
        private async Task<List<Candlestick>> DownloadCandles(string symbol, DateTime startTime, DateTime endTime)
        {
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
                            StartTime = startTime,
                            Interval = KlineInterval.OneMinute,
                            EndTime = endTime,
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

        public int ConcurrencyCount { get; set; } = 10;
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
                            this.UpdateFirstKnownData(metaData.Info, KlineToCandlestick(firstAvailable));
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
