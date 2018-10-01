using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestCombinedWebSocketClient
    {
        public static void Test()
        {
            HistoricalRateDataBase histDb = new HistoricalRateDataBase(".\\Data2");
            BinanceMarketApi api = new BinanceMarketApi(null, null, histDb);


            CombinedWebSocketClient cli = new CombinedWebSocketClient();
            var symbols = api.GetSymbols().ToArray();
            Stopwatch sw = new Stopwatch();

            List<string> remaining = new List<string>(symbols.Select(s => s.Symbol));
            var subscriber = new asd();
            foreach (var sym in symbols)
            {
                //cli.SubscribePartialDepthStream(sym.Symbol, BinanceExchange.API.Enums.PartialDepthLevels.Five,
                //      d => Console.WriteLine(sym.Symbol + "data"));

                //cli.SubscribeKlineStream(sym.Symbol, KlineInterval.OneMinute, d =>
                //{
                //    if (d.Kline.IsBarFinal)
                //    {
                //        Console.WriteLine($"{DateTime.Now:mm.ss.fff} - {sym.Symbol}");
                //        if (remaining.Contains(d.Symbol))
                //            remaining.Remove(d.Symbol);
                //        sw.Restart();
                //    }
                //});
                cli.SubscribeKlineStream(sym.Symbol, KlineInterval.OneMinute, subscriber.Handle);
            }

            while (true)
            {
                Thread.Sleep(50);

                if (sw.ElapsedMilliseconds > 5000)
                {
                    foreach (var sym in remaining)
                        Console.Write(sym + ",");
                    Console.WriteLine();
                    remaining = new List<string>(symbols.Select(s => s.Symbol));
                    sw.Reset();
                    cli.Unsubscribe<BinanceKlineData>(subscriber.Handle);
                }
            }

        }

        public class asd
        {
            public void Handle(BinanceKlineData data)
            {
                if (data.Kline.IsBarFinal)
                    Console.WriteLine($"{DateTime.Now:mm.ss.fff} - {data.Symbol}");
            }
        }
    }
}
