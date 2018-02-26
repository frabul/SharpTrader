using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestBinanceMarketApi
    {
        public static void Test()
        {
            BinanceMarketApi2 api2 = new BinanceMarketApi2("lwOvXYST3yLo2SMa1yMmWwZMhu4AtdU3ucWniOgCR0VmPJwEBUUlQhUZoy46LTJ4", "C2NcwKdkSEqIgDkRkWWqpZKidUTtQG5b1SNWlOrapkRs5uJ7Owvp9dHjzuMus3s3");
            var bal = api2.GetBalance("ETH");
            var prec = api2.GetSymbolPrecision("ETHBTC");
            var feed = api2.GetSymbolFeed("ADAETH");
            feed.OnTick += Feed_OnTick;
        }

        private static void Feed_OnTick(ISymbolFeed obj)
        {
            Console.WriteLine($"Tick {obj.Ask} - {obj.Bid}");
        }
    }
}
