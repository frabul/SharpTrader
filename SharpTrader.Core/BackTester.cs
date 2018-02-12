using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class BackTester
    {
        private MultiMarketSimulator Simulator;
        private TraderBot Bot { get; set; }

        public bool Started { get; private set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string BaseAsset { get; set; }
        public List<(DateTime time, decimal bal)> EquityHistory { get; set; }
        public decimal FinalBalance { get; set; }
        public decimal MaxDrowDown { get; private set; }
        public decimal MaxDrowDownPrc { get; private set; }

        ILogger Logger { get; set; }

        public BackTester(MultiMarketSimulator simulator, TraderBot bot)
        {
            Simulator = simulator;
            Bot = bot;
        }

        public void Start()
        {
            if (Started)
                return;
            Started = true;

            Bot.Start();

            bool raiseEvents = false;
            int steps = 1;
            decimal BalancePeak = 0;


            var startingBal = Simulator.GetEquity(BaseAsset);
            while (Simulator.NextTick(raiseEvents) && Simulator.Time < EndTime)
            {
                raiseEvents = StartTime <= Simulator.Time;
                if (steps % 240 == 0 && raiseEvents)
                {
                    //if (chartVM == null) 
                    //    chartVM = TraderBotResultsPlotViewModel.RunWindow(bots[0]);  
                    //chartVM.UpdateChart();
                    //Console.ReadLine();
                }
                steps++;

                var balance = Simulator.GetEquity(BaseAsset);
                BalancePeak = balance > BalancePeak ? balance : BalancePeak;
                if (BalancePeak - balance > MaxDrowDown)
                {
                    MaxDrowDown = BalancePeak - balance;
                    MaxDrowDownPrc = MaxDrowDown / BalancePeak;
                }
            }

            var totalBal = Simulator.GetEquity(BaseAsset);
            var lostInFee = Simulator.Trades.Select(tr => tr.Fee).Sum();

            var totalBuys = Simulator.Trades.Where(tr => tr.Type == TradeType.Buy).Count();

            Logger?.LogInfo($"Balance: {totalBal} - Trades:{Simulator.Trades.Count()} - Lost in fee:{lostInFee}");
            Logger?.LogInfo($"Profit/buy: {(totalBal - startingBal) / totalBuys:F8} - MaxDrawDown:{MaxDrowDown} - Max DD %:{MaxDrowDownPrc * 100}");

            //foreach (var bot in theBots)
            //{
            //    var vm = TraderBotResultsPlotViewModel.RunWindow(bot);
            //    vm.UpdateChart();
            //} 
        }
    }

    public class Optimizer
    {
        public Func<IMarketsManager, TraderBot> BotFactory { get; set; }
        public Func<MultiMarketSimulator> MarketFactory { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string BaseAsset { get; set; }

        public ILogger Logger { get; set; }
        public void Start()
        {
            var dummyMarket = MarketFactory();
            var dummyBot = BotFactory(dummyMarket);
            dummyBot.Start();

            List<int[]> optimizationArray = dummyBot.GetOptimizePermutations();

            foreach (var arr in optimizationArray)
            {
                var sim = MarketFactory();
                var bot = BotFactory(sim);
                bot.OptimizationArray = arr;
                var startingEquity = sim.GetEquity(BaseAsset);
                var backTester = new BackTester(sim, bot) { };
                backTester.StartTime = StartTime;
                backTester.EndTime = EndTime;
                backTester.BaseAsset = BaseAsset;

                backTester.Start();
                //collect info 
                var totalBal = sim.GetEquity(BaseAsset);

                Logger?.LogInfo($"Optimization array:{ JsonConvert.SerializeObject(arr) }");
                Logger?.LogInfo($"Balance: {sim.GetEquity(BaseAsset)} - Trades:{sim.Trades.Count()}");
                Logger?.LogInfo($"Profit/buy: {(totalBal - startingEquity) / sim.Trades.Count():F8} - MaxDrawDown:{backTester.MaxDrowDown} - Max DD %:{backTester.MaxDrowDownPrc * 100}");
            }
        }
    }


}
