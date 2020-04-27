using BinanceExchange.API.Client;
using BinanceExchange.API.Websockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestBinanceMarketApi
    {
        public static void Test()
        {
            
        }

        private static void Feed_OnTick(ISymbolFeed obj)
        {
            Console.WriteLine($"Tick {obj.Ask} - {obj.Bid}");
        }
    }
}
