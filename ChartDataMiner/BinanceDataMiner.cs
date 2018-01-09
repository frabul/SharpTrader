using Binance.API.Csharp.Client;
using Binance.API.Csharp.Client.Models.Market;
using Binance.API.Csharp.Client.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader;

namespace ChartDataMiner
{
    class BinanceDataMiner
    {
        private const string MarketName = "Binance";
        private ApiClient ApiClient;
        private BinanceClient Client;
        private HistoricalRateDataBase HistoryDB;



        public void MineAllBTC()
        {
            ApiClient = new ApiClient("", "");
            Client = new BinanceClient(ApiClient);
            HistoryDB = new HistoricalRateDataBase();

            IEnumerable<SymbolPrice> prices = Client.GetAllPrices().Result;
            var symbols = prices.Where(sp => sp.Symbol.EndsWith("BTC")).Select(sp => sp.Symbol);

            foreach (var symbol in symbols)
                DownloadCompleteSymbolHistory(symbol);
        }

        public void DownloadCompleteSymbolHistory(string symbol)
        {
            DateTime endTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));
            List<SharpTrader.Candlestick> AllCandles = new List<SharpTrader.Candlestick>();

            //we need to convert all in UTC time 
            var startTime = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
            //try to get the start time
            var symbolHistory = HistoryDB.GetSymbolHistory(MarketName, symbol, TimeSpan.FromSeconds(60));

            if (symbolHistory.Ticks.Count > 0)
                startTime = new DateTime(symbolHistory.Ticks.LastTickTime.Ticks, DateTimeKind.Utc);


            while (AllCandles.Count < 1 || AllCandles.Last().CloseTime < endTime)
            {

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
                startTime = new DateTime((AllCandles[AllCandles.Count - 1].CloseTime + TimeSpan.FromSeconds(1)).Ticks, DateTimeKind.Utc);

            }
            //---
            HistoryDB.AddCandlesticks(MarketName, symbol, AllCandles);
            HistoryDB.Save(MarketName, symbol, AllCandles.First().Timeframe);
        }
    }
}
