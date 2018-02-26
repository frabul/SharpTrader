
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader;
using BinanceExchange.API.Client;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Enums;

namespace SharpTrader.Utils
{
    public class BinanceDataDownloader
    {
        private const string MarketName = "Binance";



        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;
        private string DataDir;
        public BinanceDataDownloader(string dataDir)
        {
            DataDir = dataDir;

            Client = new BinanceClient(new ClientConfiguration { EnableRateLimiting = true });
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
            var symbols = Client.GetExchangeInfo().Result.Symbols;
            var toDownload = symbols.Where(sp => sp.Symbol.EndsWith("BTC")).Select(sp => sp.Symbol);
            toDownload = toDownload.Concat(symbols.Where(sp => sp.Symbol.EndsWith("USDT")).Select(sp => sp.Symbol));
            foreach (var symbol in toDownload)
                DownloadCompleteSymbolHistory(symbol, TimeSpan.FromDays(1));
        }

        public void DownloadCompleteSymbolHistory(string symbol, TimeSpan preload)
        {
            Console.WriteLine($"Downloading history for {symbol}");
            DateTime endTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));
            List<SharpTrader.Candlestick> AllCandles = new List<SharpTrader.Candlestick>();

            //we need to convert all in UTC time 
            var startTime = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
            //try to get the start time
            var symbolHistory = HistoryDB.GetSymbolHistory(MarketName, symbol, TimeSpan.FromSeconds(60));

            if (symbolHistory.Ticks.Count > 0)
                startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(preload);


            while (AllCandles.Count < 1 || AllCandles.Last().CloseTime < endTime)
            {
                Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                var candles = Client.GetKlinesCandlesticks(new GetKlinesCandlesticksRequest
                {
                    Symbol = symbol,
                    StartTime = startTime,
                    Interval = KlineInterval.OneMinute,
                    EndTime = DateTime.MaxValue
                }).Result;
                System.Threading.Thread.Sleep(60);
                var batch = candles.Select(
                    c => new SharpTrader.Candlestick()
                    {
                        Open = (double)c.Open,
                        High = (double)c.High,
                        Low = (double)c.Low,
                        Close = (double)c.Close,
                        OpenTime = c.OpenTime,
                        CloseTime = c.CloseTime.AddMilliseconds(1),
                        Volume = (double)c.QuoteAssetVolume
                    }).ToList();

                AllCandles.AddRange(batch);
                if (AllCandles.Count > 1)
                    startTime = new DateTime((AllCandles[AllCandles.Count - 1].CloseTime + TimeSpan.FromSeconds(1)).Ticks, DateTimeKind.Utc); 
            }
            //---
            HistoryDB.AddCandlesticks(MarketName, symbol, AllCandles);
            HistoryDB.ValidateData(MarketName, symbol, TimeSpan.FromSeconds(60));
            HistoryDB.Save(MarketName, symbol, AllCandles.First().Timeframe);
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

    }
}
