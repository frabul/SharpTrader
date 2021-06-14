using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using SharpTrader;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestConsole
{
    //[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 1, targetCount: 1 , invo )]
    public class HistoryDB_Benchmark
    {
        private BinanceTradeBarsRepository Benchmark_DbV2;
        private BinanceTradeBarsRepository Benchmark_DbV3;
        private SymbolHistoryId BenchMark_Symbol;
        DateTime startTime = new DateTime(2020, 06, 01);
        DateTime endTime = new DateTime(2021, 06, 01);


        public static void Run()
        {
            var summary = BenchmarkRunner.Run<HistoryDB_Benchmark>();
            Console.ReadLine();
        }
        public static void DebugMe()
        {
            BenchmarkSwitcher.FromAssembly(typeof(HistoryDB_Benchmark).Assembly).Run(Array.Empty<string>(), new DebugInProcessConfig());
        }

        [GlobalSetup]
        public async Task BenchMark_Init()
        {
            Benchmark_DbV2 = new BinanceTradeBarsRepository(@"C:\projects\temp\Data2", ChunkFileVersion.V2, ChunkSpan.OneMonth);
            Benchmark_DbV3 = new BinanceTradeBarsRepository(@"C:\projects\temp\Data3", ChunkFileVersion.V3, ChunkSpan.OneMonth);
            BenchMark_Symbol = new SymbolHistoryId("Binance", "ETHBTC", TimeSpan.FromSeconds(60));

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


        [Benchmark(Baseline = true)]
        [IterationSetup(Target = nameof(DbV2_GetData))]
        public void DbV2_Save() => Benchmark_DbV2.SaveAndClose(BenchMark_Symbol);

        [Benchmark]
        [IterationSetup(Target = nameof(DbV3_GetData))]
        public void DbV3_Save() => Benchmark_DbV3.SaveAndClose(BenchMark_Symbol);

        [Benchmark]
        [IterationSetup(Target = nameof(DbV2_Save))]
        public void DbV2_GetData()
        {
            Benchmark_DbV2.GetSymbolHistory(BenchMark_Symbol, startTime, endTime);
        }

        [Benchmark]
        [IterationSetup(Target = nameof(DbV3_Save))]
        public void DbV3_GetData()
        {
            Benchmark_DbV3.GetSymbolHistory(BenchMark_Symbol, startTime, endTime);
        }
    }


}
