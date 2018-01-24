using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestBinanceMarketApi
    {
        private static BinanceMarketApi api;

        public static void Test()
        {
            api = new BinanceMarketApi("4b8ZRyZQtZpbtELYgOOKU5PzG7AYgmbnvqAqSC53FkVItNSuf5miouB2iKyCuMjo", "CrFsvWUi0r7SzL6GPxIv63tEZW7WCsGANEKCSD0lmDr4xxdn134j1Id8HlKkCu15");
            api.Test = true;
            var omg = api.GetSymbolFeed("ADABTC");
            omg.OnTick += Omg_OnTick;
            var result = api.MarketOrder("ADABTC", TradeType.Buy, 20);
            while (true)
                System.Threading.Thread.Sleep(50);
        }

        private static void Omg_OnTick(ISymbolFeed obj)
        {
            Console.WriteLine($"New tick: {obj.Symbol} - Ask: {obj.Ask} - Bid: {obj.Bid} - Spread {obj.Spread / obj.Bid * 100:F2}");

        }
    }
}
