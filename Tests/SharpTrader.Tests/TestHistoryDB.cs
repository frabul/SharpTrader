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
        private HistoricalRateDataBase HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            HistoryDB = new HistoricalRateDataBase(DataDir);

            foreach (var (market, symbol, time) in HistoryDB.ListAvailableData())
            {
                var data = HistoryDB.GetSymbolHistory(market, symbol, time);
                List<ICandlestick> candles = new List<ICandlestick>();
                while (data.Ticks.Next())
                    candles.Add(data.Ticks.Tick);


                Console.WriteLine($"Validate before shuffle  {market} - {symbol} - {time} ");
                HistoryDB.ValidateData(market, symbol, time);



                Console.WriteLine($"Validate after shuffle {market} - {symbol} - {time} ");
                HistoryDB.Delete(market, symbol, time);
                Shuffle(candles);
                HistoryDB.AddCandlesticks(market, symbol, candles);
                HistoryDB.ValidateData(market, symbol, time);
                HistoryDB.Save(market, symbol, time);
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
