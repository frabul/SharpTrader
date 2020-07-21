using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestHistoryDB
    {
        private const string MarketName = "Binance";
        private TradeBarsRepository HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            HistoryDB = new TradeBarsRepository(DataDir);

            foreach (var histInfo in HistoryDB.ListAvailableData())
            {
                var data = HistoryDB.GetSymbolHistory(histInfo, DateTime.MinValue);
                List<Candlestick> candles = new List<Candlestick>();
                while (data.Ticks.MoveNext())
                    candles.Add(new Candlestick(data.Ticks.Current));
                Console.WriteLine($"Validate before shuffle  {histInfo.Market} - {histInfo.Symbol} - {histInfo.Timeframe} ");
                HistoryDB.ValidateData(histInfo);
                Console.WriteLine($"Validate after shuffle {histInfo.Market} - {histInfo.Symbol} - {histInfo.Timeframe}  ");
                HistoryDB.Delete(histInfo.Market, histInfo.Symbol, histInfo.Timeframe);
                Shuffle(candles);
                HistoryDB.AddCandlesticks(histInfo, candles);
                HistoryDB.ValidateData(histInfo);
                HistoryDB.SaveAndClose(histInfo);
            }
        }


        void Shuffle<T>(List<T> a)
        {
            Random Random = new Random();
            // Loops through array
            for (int i = a.Count - 1; i > 0; i--)
            {
                // Randomize a number between 0 and i (so that the range decreases each time)
                int rnd = Random.Next(0, i);

                // Save the value of the current i, otherwise it'll overright when we swap the values
                T temp = a[i];

                // Swap the new and old values
                a[i] = a[rnd];
                a[rnd] = temp;
            }
        }
    }
}
