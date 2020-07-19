
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader;
using BinanceExchange.API.Client;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Response.Error;
using System.Diagnostics;
using BinanceExchange.API.Models.Response;
using System.Threading;

namespace SharpTrader.BrokersApi.Binance
{
    public class BinanceDataDownloader
    {
        private const string MarketName = "Binance";

        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;
        private string DataDir;
        private Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();
       

        public int ConcurrencyCount { get; set; } = 10;
        public BinanceDataDownloader(string dataDir, double rateLimitFactor = 0.6f)
        {
            DataDir = dataDir;

            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });

            HistoryDB = new HistoricalRateDataBase(DataDir);

            //HistoryDB.FixDatabase((symbol, startTime, endTime) => DownloadCandles(symbol, startTime, endTime).Result.ToArray());
            //HistoryDB.FixDatabase(null);
        }


        public BinanceDataDownloader(HistoricalRateDataBase db, double rateLimitFactor = 0.6f)
        {
            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });
            HistoryDB = db;
        }

        public BinanceDataDownloader(HistoricalRateDataBase db, BinanceClient cli)
        {
            Client = cli;
            HistoryDB = db;
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
                (var firstKnowCandle, var lastKnownCandle) = HistoryDB.GetFirstAndLastCandles(new HistoryInfo(MarketName, symbol, TimeSpan.FromSeconds(60)));
                if (firstKnowCandle != null && lastKnownCandle != null)
                {
                    //if startTime is after the first tick then we start downloading from the last tick to avoid holes
                    if (startTime > firstKnowCandle.OpenTime)
                        startTime = new DateTime(lastKnownCandle.OpenTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);
                    else
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
                        //if we already downloaded the first available we can start download from the last known
                        if (firstAvailable != null && firstAvailable.CloseTime.AddSeconds(1) >= firstKnowCandle.OpenTime)
                            startTime = new DateTime(lastKnownCandle.Time.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);
                        //otherwise we leave start time as it is to avoid creating holes ( //todo optimize adding end time = firstKnowCandle.OpenTme )
                    }
                }

                var candlesDownloaded = await DownloadCandles(symbol, startTime, endTime);
                //---
                HistoryDB.AddCandlesticks(MarketName, symbol, candlesDownloaded);
                var histInfo = new HistoryInfo(MarketName, symbol, TimeSpan.FromSeconds(60));
                HistoryDB.ValidateData(histInfo);
                histInfo.Timeframe = candlesDownloaded.First().Timeframe;
                HistoryDB.SaveAndClose(histInfo);
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

        private async Task<List<Candlestick>> DownloadCandles(string symbol, DateTime startTime, DateTime endTime)
        {
            bool noMoreData = false;
            int zeroCount = 0;
            List<Candlestick> allCandles = new List<SharpTrader.Candlestick>();
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

                    var batch = candles.Select(
                        c => new SharpTrader.Candlestick()
                        {
                            Open = (double)c.Open,
                            High = (double)c.High,
                            Low = (double)c.Low,
                            Close = (double)c.Close,
                            OpenTime = c.OpenTime,
                            CloseTime = c.OpenTime.AddSeconds(60), //+ c.CloseTime.AddMilliseconds(1),
                            QuoteAssetVolume = (double)c.QuoteAssetVolume
                        }).ToList();

                    allCandles.AddRange(batch);

                    //if we get no data for more than 3 requests then we can assume that there isn't any more data
                    if (batch.Count < 1)
                        zeroCount++;
                    else
                        zeroCount = 0;
                    if (zeroCount > 2)
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
                    await Task.Delay(5000);
                }
                if (allCandles.Count > 1)
                    startTime = new DateTime((allCandles[allCandles.Count - 1].CloseTime - TimeSpan.FromSeconds(1)).Ticks, DateTimeKind.Utc);
            }

            return allCandles;
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

        private SemaphoreSlim GetSemaphore(string symbol)
        {
            if (!Semaphores.ContainsKey(symbol))
                Semaphores.Add(symbol, new SemaphoreSlim(1, 1));
            return Semaphores[symbol];
        }
    }
}
