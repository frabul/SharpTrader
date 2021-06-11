using SharpTrader;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        { 
            DateTime time = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<Candlestick> candles = new List<Candlestick>();
            for (int i = 0; i < 100; i++)
            {
                time.AddMinutes(1);
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
            hc.Save("testFile2.bin3");
            Thread.Sleep(100);
            var loaded = HistoryChunkV3.Load("cribbio.bpack");

            Console.ReadLine();

            //--------------------
            SharpTrader.Tests.TestHistoryDB.Run();
        }
    }


}
