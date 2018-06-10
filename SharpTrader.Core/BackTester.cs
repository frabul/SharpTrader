using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        NLog.Logger Logger { get; set; }

        public BackTester(MultiMarketSimulator simulator, TraderBot bot)
        {
            Simulator = simulator;
            Bot = bot;
        }

        public void Start()
        {
            Logger = NLog.LogManager.GetLogger("BackTester");
            Simulator.StartOfSimulation = StartTime - TimeSpan.FromDays(10);
            if (Started)
                return;
            Started = true;

            Bot.Start().Wait();

            bool raiseEvents = false;
            int steps = 1;
            decimal BalancePeak = 0;


            var startingBal = Simulator.GetEquity(BaseAsset);
            while (Simulator.NextTick(raiseEvents) && Simulator.Time < EndTime)
            {
                if (raiseEvents)
                    Bot.OnTick().Wait();
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

            Logger.Info($"Balance: {totalBal} - Trades:{Simulator.Trades.Count()} - Lost in fee:{lostInFee}");
            if (totalBuys > 0)
                Logger.Info($"Profit/buy: {(totalBal - startingBal) / totalBuys:F8} - MaxDrawDown:{MaxDrowDown} - Max DD %:{MaxDrowDownPrc * 100}");

            //foreach (var bot in theBots)
            //{
            //    var vm = TraderBotResultsPlotViewModel.RunWindow(bot);
            //    vm.UpdateChart();
            //} 
        }
    }

    public class Optimizer
    {
        public Func<IMarketsManager, OptimizationSpace, TraderBot> BotFactory { get; set; }
        public Func<MultiMarketSimulator> MarketFactory { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string BaseAsset { get; set; }
        private NLog.Logger Logger;

        private OptimizationSpace BaseSpace;

        public void Start()
        {
            Logger = NLog.LogManager.GetLogger("Optimizer");

            BaseSpace = new OptimizationSpace();

            var dummyMarket = MarketFactory();
            var dummyBot = BotFactory(dummyMarket, BaseSpace);
            var paramSets = BaseSpace.GetPermutations();

            void act(OptimizationSpace paramSet)
            {
                var sim = MarketFactory();
                var bot = BotFactory(sim, paramSet);
                var startingEquity = sim.GetEquity(BaseAsset);
                var backTester = new BackTester(sim, bot)
                {
                    StartTime = StartTime,
                    EndTime = EndTime,
                    BaseAsset = BaseAsset,
                };

                var tostr = "";
                foreach (var pp in paramSet.ParamsSet)
                    tostr += $"{pp.prop}: {pp.val} | ";
                var msg = $"\nOptimization array: { tostr }";
                Logger.Info($"\n--------------- {bot.ToString()} -------------------\n" + msg);

                backTester.Start();
                //collect info 

            }
            foreach (var ps in paramSets)
                act(ps);
            //Parallel.ForEach(paramSets, new ParallelOptions { MaxDegreeOfParallelism = 2 }, act);
        }
    }

    public class OptimizationSpace
    {
        private List<(string prop, object[] pars)> Values = new List<(string, object[])>();
        private List<int> OptimizationIndexes = new List<int>();
        private int Cursor = -1;
        public IEnumerable<(string prop, object[] pars)> Space => Values;
        public IEnumerable<(string prop, object val)> ParamsSet
        {
            get
            {
                for (int i = 0; i < Values.Count; i++)
                {
                    yield return (Values[i].prop, Values[i].pars[OptimizationIndexes[i]]);
                }
            }
        }

        public T Optimize<T>(string property, T[] values)
        {
            Cursor++;
            if (Values.Count <= Cursor)
            {
                Values.Add((property, values.Cast<object>().ToArray()));
                OptimizationIndexes.Add(0);
            }
            else
            {
                //check that values are equal
                Debug.Assert(property.Equals(Values[Cursor].prop));
                Debug.Assert(values.Length == Values[Cursor].pars.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    Debug.Assert(values[i].Equals(Values[Cursor].pars[i]));
                }

            }
            return (T)Values[Cursor].pars[OptimizationIndexes[Cursor]];
        }

        public IEnumerable<OptimizationSpace> GetPermutations()
        {
            List<int[]> permutations = new List<int[]>();
            int[] currentPerm = new int[Values.Count];


            bool Increment(int i)
            {
                if (i == currentPerm.Length)
                    return false; //we created all possible permutations

                currentPerm[i] += 1;
                if (currentPerm[i] == Values[i].pars.Length)
                {
                    currentPerm[i] = 0;
                    return Increment(i + 1);
                }
                else
                    return true;
            }
            permutations.Add(currentPerm.ToArray());
            while (Increment(0))
            {
                permutations.Add(currentPerm.ToArray());
            }
            var spaces = permutations
                .Select(p => new OptimizationSpace()
                {
                    Values = this.Values,
                    OptimizationIndexes = p.ToList(),
                });
            return spaces;
        }


    }
}
