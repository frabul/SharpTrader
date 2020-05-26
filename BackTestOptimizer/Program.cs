using SharpTrader;
using SharpTrader.Algos;
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

        static void Main(string[] args)
        {
 

            Optimizer.Configuration conf = new Optimizer.Configuration
            {
                SessionName = "Hpm2",
                BacktesterConfig = new BackTester.Configuration
                {
                    DataDir = @"D:\ProgettiBck\SharpTraderBots\Bin\Data",
                    HistoryDb = @"D:\ProgettiBck\SharpTraderBots\Bin\Data",
                    Market = "Binance",
                    StartingBalance = new AssetAmount("BTC", 100), 
                    StartTime = new DateTime(2020, 02, 01),
                    EndTime = new DateTime(2020, 05, 30),
                    AlgoClass = typeof(HighPassMeanReversionAlgo2).AssemblyQualifiedName,
                }, 
            };
            Optimizer optim = new Optimizer(conf, ShowPlotCallback);
            optim.Start();
        } 
    }
}
