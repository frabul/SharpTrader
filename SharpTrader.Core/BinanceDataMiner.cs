using Binance.API.Csharp.Client;
using Binance.API.Csharp.Client.Models.Market;
using Binance.API.Csharp.Client.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader;

namespace SharpTrader.Utils
{
    public class BinanceDataDownloader
    {
        private const string MarketName = "Binance";


        private ApiClient ApiClient;
        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;
        private string DataDir;
        public BinanceDataDownloader(string dataDir)
        {
            DataDir = dataDir;
            ApiClient = new ApiClient("", "");
            Client = new BinanceClient(ApiClient, false);
            HistoryDB = new HistoricalRateDataBase(DataDir);
        }
        public BinanceDataDownloader(HistoricalRateDataBase db)
        {

            ApiClient = new ApiClient("", "");
            Client = new BinanceClient(ApiClient, false);
            HistoryDB = db;
        }
        public void MineBinance()
        {
            IEnumerable<SymbolPrice> prices = Client.GetAllPrices().Result;
            var symbols = prices.Where(sp => sp.Symbol.EndsWith("BTC")).Select(sp => sp.Symbol);
            symbols = symbols.Concat(prices.Where(sp => sp.Symbol.EndsWith("USDT")).Select(sp => sp.Symbol));
            foreach (var symbol in symbols)
                DownloadCompleteSymbolHistory(symbol);
        }

        public void DownloadCompleteSymbolHistory(string symbol)
        {
            Console.WriteLine($"Downloading history for {symbol}");
            DateTime endTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));
            List<SharpTrader.Candlestick> AllCandles = new List<SharpTrader.Candlestick>();

            //we need to convert all in UTC time 
            var startTime = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
            //try to get the start time
            var symbolHistory = HistoryDB.GetSymbolHistory(MarketName, symbol, TimeSpan.FromSeconds(60));

            if (symbolHistory.Ticks.Count > 0)
                startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc).Subtract(TimeSpan.FromHours(48));


            while (AllCandles.Count < 1 || AllCandles.Last().CloseTime < endTime)
            {
                Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                var candles = Client.GetCandleSticks(symbol, TimeInterval.Minutes_1, startTime).Result;
                System.Threading.Thread.Sleep(60);
                var batch = candles.Select(
                    c => new SharpTrader.Candlestick()
                    {
                        Open = (double)c.Open,
                        High = (double)c.High,
                        Low = (double)c.Low,
                        Close = (double)c.Close,
                        OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(c.OpenTime).UtcDateTime,
                        CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(c.OpenTime).UtcDateTime + TimeSpan.FromSeconds(60),
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
            var tradingRules = Client.GetTradingRulesAsync().Result;
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.SymbolName, (symb.BaseAsset, symb.QuoteAsset));

            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, (string Asset, string Quote)>>(json);
            System.IO.File.WriteAllText(DataDir + "BinanceSymbolsTable.json", json);
        }

    }
}
