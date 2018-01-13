using SharpTrader.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestMarketSimulator
    {
        private const string MarketName = "Binance";
        private HistoricalRateDataBase HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            HistoryDB = new HistoricalRateDataBase(DataDir);
            MultiMarketSimulator simulator = new MultiMarketSimulator(DataDir, HistoryDB);
            var binanceMarket = simulator.GetMarketApi("Binance");

            //var ETHBTC = binanceMarket.GetSymbolFeed("ETHBTC");
            //var XMRBTC = binanceMarket.GetSymbolFeed("XMRBTC");
            TestBot tester = new TestBot(simulator);
            tester.Start();
            simulator.Run(
                new DateTime(2017, 12, 01),
                new DateTime(2018, 1, 01),
                DateTime.MinValue
                );

            foreach (var feed in binanceMarket.ActiveFeeds)
            {
                var balance = binanceMarket.GetBalance(feed.Asset);
                if (balance > 0)
                    binanceMarket.MarketOrder(feed.Symbol, TradeType.Sell, balance);
            }

            foreach (var (Symbol, balance) in binanceMarket.Balances)
            {
                 
                Console.WriteLine($"{Symbol }: {balance}");
            }

            Console.ReadLine();
        }


    }
}
