
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

namespace SharpTrader.Utils
{
    public class BinanceDataDownloader
    {
        private const string MarketName = "Binance";



        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;
        private string DataDir;
        private int MaxRequestsPerMinute = 1200;
        private static Queue<DateTime> RequestsPer30Seconds = new Queue<DateTime>();
        private static object QueueLock = new object();


        public BinanceDataDownloader(string dataDir)
        {
            DataDir = dataDir;

            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, });

            HistoryDB = new HistoricalRateDataBase(DataDir);
        }
        public BinanceDataDownloader(HistoricalRateDataBase db)
        {
            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, });
            HistoryDB = db;
        }

        public BinanceDataDownloader(HistoricalRateDataBase db, BinanceClient cli)
        {
            Client = cli;
            HistoryDB = db;
        }

        public void MineBinance()
        {
            var exchangeInfo = Client.GetExchangeInfo().Result;
            var symbols = exchangeInfo.Symbols;

            var toDownload = symbols
                .Where(sp => sp.Symbol.EndsWith("ETH"))// || sp.Symbol.EndsWith("USDT"))
                .Select(sp => sp.Symbol).ToList();
            var downloadQueue = new Queue<string>(toDownload);
            List<Task> tasks = new List<Task>();
            Stopwatch swReportRate = new Stopwatch();
            swReportRate.Start();
            while (downloadQueue.Count > 0)
            {
                if (tasks.Count < 10)
                {
                    var symbol = downloadQueue.Dequeue();
                    var task = DownloadHistoryAsync(symbol, DateTime.UtcNow.Subtract(TimeSpan.FromDays(360)), TimeSpan.FromDays(1));
                    tasks.Add(task);

                }
                foreach (var t in tasks.Where(t => t.IsCompleted).ToArray())
                {
                    tasks.Remove(t);
                    if (t.Exception != null)
                        Console.WriteLine("A task terminated with an exception");
                }
                if (swReportRate.ElapsedMilliseconds > 10000)
                {
                    Console.WriteLine($"Current rate {RequestsPer30Seconds.Count * 2} requests/min");
                    swReportRate.Restart();
                }

                System.Threading.Thread.Sleep(1);
            }
            while (tasks.Any(t => !t.IsCompleted))
                System.Threading.Thread.Sleep(1);
        }

        public async Task DownloadHistoryAsync(string symbol, DateTime fromTime, TimeSpan redownloadStart)
        {
            try
            {
                Console.WriteLine($"Downloading {symbol} history ");
                DateTime endTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));
                List<SharpTrader.Candlestick> AllCandles = new List<SharpTrader.Candlestick>();

                //we need to convert all in UTC time 
                var startTime = fromTime; //  
                var epoch = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
                if (startTime < epoch)
                    startTime = epoch;

                //try to get the start time
                var symbolHistory = HistoryDB.GetSymbolHistory(MarketName, symbol, TimeSpan.FromSeconds(60));


                if (symbolHistory.Ticks.Count > 0)
                {
                    if (startTime > symbolHistory.Ticks.FirstTickTime) //if startTime is after the first tick we are ready to go
                        startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);
                    else
                    {
                        await RateLimitAsync();
                        var firstCandle = (await Client.GetKlinesCandlesticks(
                            new GetKlinesCandlesticksRequest
                            {
                                Symbol = symbol,
                                StartTime = startTime,
                                Interval = KlineInterval.OneMinute,
                                EndTime = DateTime.MaxValue,
                                Limit = 10
                            })).FirstOrDefault();

                        if (firstCandle != null && firstCandle.CloseTime + TimeSpan.FromSeconds(1) >= symbolHistory.Ticks.FirstTickTime)
                            startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);

                    }
                }

                while (AllCandles.Count < 1 || AllCandles.Last().CloseTime < endTime)
                {
                    //Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                    await RateLimitAsync();
                    var candles = await Client.GetKlinesCandlesticks(new GetKlinesCandlesticksRequest
                    {
                        Symbol = symbol,
                        StartTime = startTime,
                        Interval = KlineInterval.OneMinute,
                        EndTime = DateTime.MaxValue
                    });
                    await Task.Delay(10);
                    var batch = candles.Select(
                        c => new SharpTrader.Candlestick()
                        {
                            Open = (double)c.Open,
                            High = (double)c.High,
                            Low = (double)c.Low,
                            Close = (double)c.Close,
                            OpenTime = c.OpenTime,
                            CloseTime = c.OpenTime.AddSeconds(60), //+ c.CloseTime.AddMilliseconds(1),
                            Volume = (double)c.QuoteAssetVolume
                        }).ToList();

                    AllCandles.AddRange(batch);
                    if (AllCandles.Count > 1)
                        startTime = new DateTime((AllCandles[AllCandles.Count - 1].CloseTime - TimeSpan.FromSeconds(1)).Ticks, DateTimeKind.Utc);
                }
                //---
                HistoryDB.AddCandlesticks(MarketName, symbol, AllCandles);
                HistoryDB.ValidateData(MarketName, symbol, TimeSpan.FromSeconds(60));
                HistoryDB.Save(MarketName, symbol, AllCandles.First().Timeframe);
                Console.WriteLine($"{symbol} history downloaded");
            }
            catch (Exception ex)
            {
                var msg = $"Exception during {symbol} history download: ";
                if (ex is BinanceException binException)
                    msg += binException.ErrorDetails;
                else
                    msg += ex.Message;
                Console.WriteLine(msg);
            }
        }

        public void SynchSymbolsTable()
        {
            Dictionary<string, (string Asset, string Quote)> dict = new Dictionary<string, (string Asset, string Quote)>();
            var tradingRules = Client.GetExchangeInfo().Result;
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.Symbol, (symb.BaseAsset, symb.QuoteAsset));

            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, (string Asset, string Quote)>>(json);
            System.IO.File.WriteAllText(DataDir + "BinanceSymbolsTable.json", json);
        }


        private async Task RateLimitAsync()
        {

            while (RequestsPer30Seconds.Count > (MaxRequestsPerMinute / 2))
            {
                lock (QueueLock)
                {
                    while (RequestsPer30Seconds.Count > 0 && DateTime.Now - RequestsPer30Seconds.Peek() > TimeSpan.FromSeconds(30))
                        RequestsPer30Seconds.Dequeue();
                }
                await Task.Delay(1000);
            }
            lock (QueueLock)
            {
                RequestsPer30Seconds.Enqueue(DateTime.Now);
                while (RequestsPer30Seconds.Count > 0 && DateTime.Now - RequestsPer30Seconds.Peek() > TimeSpan.FromSeconds(30))
                    RequestsPer30Seconds.Dequeue();
            }
        }
    }
}
