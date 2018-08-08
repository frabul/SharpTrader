
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

namespace SharpTrader.Utils
{
    public class BinanceDataDownloader
    {
        private const string MarketName = "Binance";

        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;
        private string DataDir;




        public BinanceDataDownloader(string dataDir, double rateLimitFactor = 0.6f)
        {
            DataDir = dataDir;

            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });

            HistoryDB = new HistoricalRateDataBase(DataDir);
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
                ISymbolHistory symbolHistory = HistoryDB.GetSymbolHistory(MarketName, symbol, TimeSpan.FromSeconds(60));

                if (symbolHistory.Ticks.Count > 0)
                {
                    if (startTime > symbolHistory.Ticks.FirstTickTime) //if startTime is after the first tick we are ready to go
                        startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);
                    else
                    {

                        var firstCandle = (await Client.GetKlinesCandlesticks(
                            new GetKlinesCandlesticksRequest
                            {
                                Symbol = symbol,
                                StartTime = startTime,
                                Interval = KlineInterval.OneMinute,
                                EndTime = startTime.AddYears(3),
                                Limit = 10
                            })).FirstOrDefault();

                        if (firstCandle != null && firstCandle.CloseTime + TimeSpan.FromSeconds(1) >= symbolHistory.Ticks.FirstTickTime)
                            startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(redownloadStart);

                    }
                }

                bool noMoreData = false;
                int zeroCount = 0;
                while (!noMoreData && ( AllCandles.Count < 1 || AllCandles.Last().CloseTime < endTime))
                {
                    //Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                    try
                    {
                        var candles = await Client.GetKlinesCandlesticks(new GetKlinesCandlesticksRequest
                        {
                            Symbol = symbol,
                            StartTime = startTime,
                            Interval = KlineInterval.OneMinute,
                            EndTime = startTime.AddYears(3),
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
                                Volume = (double)c.QuoteAssetVolume
                            }).ToList();
                         
                        AllCandles.AddRange(batch);

                        //if we get no data for more than 3 requests then we can assume that there isn't any more data
                        if (batch.Count < 1)
                            zeroCount++;
                        else
                            zeroCount = 0;
                        if (zeroCount > 3)
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
                var msg = $"Fatal Exception during {symbol} history download: ";
                if (ex is BinanceException binException)
                    msg += binException.ErrorDetails;
                else
                    msg += ex.Message;
                Console.WriteLine(msg);
            }
        }

        public void SynchSymbolsTable(string DataDir)
        {
            Dictionary<string, SymbolInfo> dict = new Dictionary<string, SymbolInfo>();
            var tradingRules = Client.GetExchangeInfo().Result;
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.Symbol,
                    new SymbolInfo
                    {
                        Asset = symb.BaseAsset,
                        QuoteAsset = symb.QuoteAsset,
                        Symbol = symb.Symbol
                    });

            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, SymbolInfo>>(json);
            System.IO.File.WriteAllText(DataDir + "BinanceSymbolsTable.json", json);
        }


    }
}
