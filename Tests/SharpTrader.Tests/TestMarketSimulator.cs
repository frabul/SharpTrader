using SharpTrader.Indicators;
using SharpTrader.Plotting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class TestMarketSimulator
    {

        private const string MarketName = "Binance";
        private HistoricalRateDataBase HistoryDB;

        private const string DataDir = ".\\Data\\";

        public void Test()
        {
            List<(DateTime date, decimal bal)> BalancePerDay = new List<(DateTime, decimal)>();
            HistoryDB = new HistoricalRateDataBase(DataDir);
            MultiMarketSimulator simulator = new MultiMarketSimulator(DataDir, HistoryDB);
            var api = simulator.GetMarketApi("Binance");


            TraderBot[] bots = new TraderBot[]
            {
            };

            foreach (var bot in bots)
                bot.Start();

            var simStart = new DateTime(2017, 09, 10);
            var simEnd = new DateTime(2018, 01, 28);

            bool raiseEvents = false;
            int steps = 1;
            decimal MaxDrawDown = 0;
            decimal BalancePeak = 0;
            decimal MaxDDPrc = 0;
            while (simulator.NextTick(raiseEvents) && simulator.Time < simEnd)
            {
                //var lastDay = BalancePerDay[BalancePerDay.Count - 1];

                raiseEvents = simStart <= simulator.Time;
                if (steps % 240 == 0 && raiseEvents)
                {
                    //if (chartVM == null) 
                    //    chartVM = TraderBotResultsPlotViewModel.RunWindow(bots[0]);  
                    //chartVM.UpdateChart();
                    //Console.ReadLine();
                }
                steps++;

                var balance = api.GetBtcPortfolioValue();
                BalancePeak = balance > BalancePeak ? balance : BalancePeak;
                if (BalancePeak - balance > MaxDrawDown)
                {
                    MaxDrawDown = BalancePeak - balance;
                    MaxDDPrc = MaxDrawDown / BalancePeak;
                }
            }

            //save DATASET


            var totalBal = api.GetBtcPortfolioValue();
            var lostInFee = api.Trades.Select(tr => tr.Fee).Sum();
            Console.WriteLine($"Balance: {totalBal} - Trades:{api.Trades.Count()} - Lost in fee:{lostInFee}");

            Console.WriteLine($"Profit/oper: {(totalBal - 1) / api.Trades.Count()} MaxDrawDown:{MaxDrawDown} - Max DD %:{MaxDDPrc * 100}");
            foreach (var bot in bots)
            {
                var vm = TraderBotResultsPlotViewModel.RunWindow(bot);
                vm.UpdateChart();
            }
            Console.ReadLine();
        }





        public void TestMeanAndVarianceIndicator()
        {
            TimeSerie<ICandlestick> ts = new TimeSerie<ICandlestick>();
            MeanAndVariance mv = new MeanAndVariance(5, new TimeSerieNavigator<ICandlestick>(ts));
            var mvnav = mv.GetNavigator();
            for (int i = 0; i < 10000; i++)
            {
                ts.AddRecord(new Candlestick() { Close = i % 5, OpenTime = new DateTime(i), CloseTime = new DateTime(i + 1) });
                mv.Calculate();
                if (mv.IsReady)
                {
                    Debug.Assert(mvnav.LastTick.Mean == 2 && mvnav.LastTick.Variance == 2);
                    if (mvnav.LastTick.Mean != 2)
                        Console.WriteLine("error");
                }
            }
        }




    }
}
