using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{

    public class TestHistoryDB
    {
        private const string MarketName = "Binance";
        private TradeBarsRepository HistoryDB;
        private BinanceTradeBarsRepository Benchmark_DbV2;
        private BinanceTradeBarsRepository Benchmark_DbV3;
        private SymbolHistoryId BenchMark_Symbol;
        private const string DataDir = @"D:\ProgettiBck\SharpTraderBots\Bin\Data";

        public static async Task RunAsync()
        {
            var test = new TestHistoryDB();
            await test.TestDbV3Async();
            Console.ReadLine();
        }

        public static Task RebuildDb()
        {
            var dir = "C:/projects/temp/bigdb/";
            var dbv2 = Path.Combine(dir, "RatesDB/DatabaseV2.db");
            var dbv3 = Path.Combine(dir, "RatesDB/DatabaseV3.db");
            if (File.Exists(dbv2))
                File.Delete(dbv2);
            if (File.Exists(dbv3))
                File.Delete(dbv3);
            Stopwatch sw = Stopwatch.StartNew();
            var chunks = TradeBarsRepository.DiscoverChunks(dir + "RatesDB/");
            sw.Report("DiscoverChunks init");

            sw.Restart();
            var db = new TradeBarsRepository(dir);
            sw.Report("Repo init");

            Console.WriteLine("Press Enter to continue.");
            Console.ReadLine();
            return Task.CompletedTask;
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


        public async Task TestDbV3Async()
        {
            Benchmark_DbV2 = new BinanceTradeBarsRepository(@"C:\projects\temp\Data2", ChunkFileVersion.V2, ChunkSpan.OneMonth);
            Benchmark_DbV3 = new BinanceTradeBarsRepository(@"C:\projects\temp\Data3", ChunkFileVersion.V3, ChunkSpan.OneMonth);
            BenchMark_Symbol = new SymbolHistoryId("Binance", "ETHBTC", TimeSpan.FromSeconds(60));
            var startTime = new DateTime(2020, 06, 01);
            var endTime = new DateTime(2021, 06, 01);
            await Benchmark_DbV2.AssureData(BenchMark_Symbol, startTime, endTime);

            var data = Benchmark_DbV2.GetSymbolHistory(BenchMark_Symbol, startTime, endTime);

            List<Candlestick> candles = new List<Candlestick>();
            while (data.Ticks.MoveNext())
                candles.Add(new Candlestick(data.Ticks.Current));

            Benchmark_DbV3.AddCandlesticks(BenchMark_Symbol, candles);
            Benchmark_DbV2.SaveAndClose(BenchMark_Symbol);
            Benchmark_DbV3.SaveAndClose(BenchMark_Symbol);

            var data3 = Benchmark_DbV3.GetSymbolHistory(BenchMark_Symbol, startTime, endTime);

            data.Ticks.SeekFirst();
            data3.Ticks.SeekFirst();

            do
            {
                var tick1 = data.Ticks.Current;
                var tick2 = data3.Ticks.Current;
                var ok = tick1.Equals(tick2);
                Debug.Assert(ok, $"Error at {tick1.Time}");
            } while (data.Ticks.MoveNext() && data3.Ticks.MoveNext());
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

    public static class MethodTimeLogger
    {
        public static void Log(MethodBase methodBase, TimeSpan elapsed, string message)
        {
            Console.WriteLine($"{methodBase.Name} execution took {elapsed.TotalMilliseconds} ms.");
        }
    }

    public static class Extensions
    {
        public static void Report(this Stopwatch sw, string taskName)
        {
            sw.Stop();
            Console.WriteLine($"{taskName} took {sw.Elapsed.TotalSeconds} ms.");
        }
    }

}
