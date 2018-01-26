using SharpTrader.Bots;
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


            api.Test = false;
       
            TestBot3[] bots = new TestBot3[]
            {
                new TestBot3(api){TradeSymbol = "OMGBTC"},
                new TestBot3(api){TradeSymbol = "QTUMBTC"},
                new TestBot3(api){TradeSymbol = "YOYOBTC"},
                new TestBot3(api){TradeSymbol = "ADABTC"},
                new TestBot3(api){TradeSymbol = "LTCBTC"},
            };
            foreach(var bot in bots)
                bot.Start();
            while (true)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        private static void OnTick(ISymbolFeed obj)
        {

        }
    }
}
