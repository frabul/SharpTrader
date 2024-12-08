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
    public class Program
    {
        interface IFoo
        {
            int A { get; }
        }
        class Foo : IFoo
        {
            public int A { get; set; } = 1;
            public int C { get; set; } = 2;
        }
        class Foo2 : IFoo
        {
            public int A { get; set; } = 3;
            public int D { get; set; } = 4;
        }
        class Bar
        {
            public IFoo theFoo { get; set; } = new Foo();
            public int B { get; set; } = 3;
        }

        public static async Task Main(string[] args)
        {
            await TestHistoryDB.TestFillGaps("ETHBTC");

            Console.WriteLine("Enter to continue.");
            Console.ReadLine();

            new TestTradeBarConsolidator().SimpleTest();
            LiteDB.LiteDatabase db = new LiteDB.LiteDatabase("test.db");


            Console.WriteLine("Enter to continue.");
            Console.ReadLine();

            var dummyMapper = new LiteDB.BsonMapper();
            var mapper2 = new LiteDB.BsonMapper();

            db.Mapper.RegisterType<Foo>(
                serialize: (obj) =>
                {
                    return dummyMapper.Serialize<IFoo>(obj);
                },
                deserialize: (bson) =>
                {
                    return dummyMapper.Deserialize<Foo>(bson);
                }
            );

            var bars = db.GetCollection<Bar>("FooCollection");
            bars.DeleteAll();
            bars.Upsert(new Bar());

            db.Mapper.RegisterType<IFoo>(
               serialize: (obj) =>
               {
                   return dummyMapper.Serialize(obj);
               },
               deserialize: (bson) =>
               {
                   var tofoo2 = dummyMapper.Deserialize<Foo2>(bson);
                   return new Foo() { A = bson["A"].AsInt32 };
               }
            );


            var ret1 = db.GetCollection("FooCollection").FindOne(e => true);
            //var obj = mapper2.Deserialize<Bar>(ret1);
            var ret = bars.FindAll().ToList();


            Console.WriteLine("Running charts test");
            await ChartsTest.Run();
            Console.WriteLine("Completed. Press enter to continue.");
            Console.ReadLine();

            Console.WriteLine("Running TestHistoryDB");
            await SharpTrader.Tests.TestHistoryDB.RunAsync();
            Console.WriteLine("TestHistoryDB Completed. Press enter to continue.");
            Console.ReadLine();

            Console.ReadLine();
            await TestHistoryDB.RebuildDb();
            //HistoryDB_Benchmark.Run();

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
