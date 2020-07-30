using SharpTrader;
using SharpTrader.Algos;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Plotting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackTestOptimizer
{
    class Program
    {
        static void ShowPlotCallback(PlotHelper plot)
        {
            PlottingHelper.Show(plot);
            Console.ReadLine();
        }

        static async Task Main(string[] args)
        {
            var StartTime = new DateTime(2020, 04, 01);
            var EndTime = new DateTime(2020, 07, 30);
            //var db = new BinanceTradeBarsRepository(@"D:\ProgettiBck\SharpTraderBots\Bin\Data2", rateLimitFactor: 0.4);
            //db.SynchSymbolsTable(@"D:\ProgettiBck\SharpTraderBots\Bin\Data2");

            //await db.AssureFilter(sym => sym.EndsWith("BTC"), StartTime.AddDays(-3), EndTime);
     
            Optimizer.Configuration conf = new Optimizer.Configuration
            {
                SessionName = "Hpm2",
                BacktesterConfig = new BackTester.Configuration
                {
                    DataDir = @"D:\ProgettiBck\SharpTraderBots\Bin\Data2",
                    HistoryDb = @"D:\ProgettiBck\SharpTraderBots\Bin\Data2",
                    Market = "Binance",
                    StartingBalance = new AssetAmount("BTC", 100),
                    StartTime = StartTime,
                    EndTime = EndTime,
                    AlgoClass = typeof(HighPassMeanReversionAlgo2).AssemblyQualifiedName,
                    PlotResults = false,
                    PlottingEnabled = false,
                    SessionName = "Hpm2"
                },
            };
            Optimizer optim = new Optimizer(conf, ShowPlotCallback);
            optim.Start();
        }
    }
}
