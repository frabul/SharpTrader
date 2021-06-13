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

        private const string DataDir = @"D:\ProgettiBck\SharpTraderBots\Bin\Data";

        public static void Run()
        {
            var test = new TestHistoryDB();
            test.TestValidation();
        }
        public void TestValidation()
        {
            HistoryDB = new TradeBarsRepository(DataDir);


            Console.WriteLine("Db loaded, press a key to continue");
            Console.ReadLine();

            foreach (var histInfo in HistoryDB.ListAvailableData())
            {
                var data = HistoryDB.GetSymbolHistory(histInfo, DateTime.MinValue);
                List<Candlestick> candles = new List<Candlestick>();
                while (data.Ticks.MoveNext())
                    candles.Add(new Candlestick(data.Ticks.Current));
                Console.WriteLine($"Validate before shuffle  {histInfo.Market} - {histInfo.Symbol} - {histInfo.Resolution} ");
                HistoryDB.ValidateData(histInfo);
                Console.WriteLine($"Validate after shuffle {histInfo.Market} - {histInfo.Symbol} - {histInfo.Resolution}  ");
                HistoryDB.Delete(histInfo.Market, histInfo.Symbol, histInfo.Resolution);
                Shuffle(candles);
                HistoryDB.AddCandlesticks(histInfo, candles);
                HistoryDB.ValidateData(histInfo);
                HistoryDB.SaveAndClose(histInfo);
            }
        }

            
        public void TestDbV3()
        {
            TradeBarsRepository dbv2 = new TradeBarsRepository(@"D:\ProgettiBck\SharpTraderBots\Bin\Data2");
            TradeBarsRepository dbv3 = new TradeBarsRepository(@"D:\ProgettiBck\SharpTraderBots\Bin\Data3");
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
