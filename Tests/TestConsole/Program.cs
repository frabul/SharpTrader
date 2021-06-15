using SharpTrader;
using SharpTrader.Storage;
using SharpTrader.Tests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await TestHistoryDB.RebuildDb();
            //HistoryDB_Benchmark.Run();
            await SharpTrader.Tests.TestHistoryDB.RunAsync();
            await TestMessagePack();
        }

        private static async Task TestMessagePack()
        {
            DateTime time = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<Candlestick> candles = new List<Candlestick>();
            for (int i = 0; i < 1000; i++)
            {
                time = time.AddMinutes(1);
                Candlestick candle = new Candlestick()
                {
                    OpenTime = time,
                    CloseTime = time.AddMinutes(1),
                    Close = i,
                };
                candles.Add(candle);
            }
            HistoryChunkV3 hc = new HistoryChunkV3()
            {
                ChunkId = new HistoryChunkIdV3(new SymbolHistoryId("meme", "BTCUSDT", TimeSpan.FromSeconds(60)), candles.First().CloseTime, candles.Last().CloseTime),
                Ticks = candles
            };
            await hc.SaveAsync("D:/");
            Thread.Sleep(100);
            var loaded = HistoryChunkV3.Load("D:/" + hc.Id.GetFileName()).Result;

            Console.ReadLine();

            var chunk = await HistoryChunk.Load(@"D:\ProgettiBck\SharpTraderBots\Bin\Data2\RatesDB\Binance_ZRXUSDT_60000_202008.bin2");
            HistoryChunkV3 newChunk = new HistoryChunkV3()
            {
                ChunkId = new HistoryChunkIdV3(chunk.ChunkId),
                Ticks = chunk.Ticks
            };
            await newChunk.SaveAsync("D:\\");
            await chunk.SaveAsync("D:\\");
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            await newChunk.SaveAsync("D:\\");
            sw.Stop();
            Console.WriteLine($"Elapsed {sw.ElapsedTicks}");
            sw.Restart();
            await chunk.SaveAsync("D:\\");
            sw.Stop();
            Console.WriteLine($"Elapsed {sw.ElapsedTicks}");
            Console.ReadLine();
        }
    }


}
