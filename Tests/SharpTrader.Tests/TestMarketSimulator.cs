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

            var ETHBTC = binanceMarket.GetSymbolFeed("ETHBTC");
            var XMRBTC = binanceMarket.GetSymbolFeed("XMRBTC");
            simulator.Run(
                new DateTime(2017, 09, 01),
                new DateTime(2017, 10, 01),
                DateTime.MinValue
                );




        }


    }
}
