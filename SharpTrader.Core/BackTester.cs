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
        private TraderBot[] Bots { get; set; }

        public bool Started { get; private set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string BaseAsset { get; set; }
        public List<(DateTime time, decimal bal)> EquityHistory { get; set; } = new List<(DateTime time, decimal bal)>();
        public decimal FinalBalance { get; set; }
        public decimal MaxDrowDown { get; private set; }
        /// <summary>
        /// How much history should be loaded back from the simulation start time
        /// </summary>
        public TimeSpan HistoryLookBack { get; private set; } = TimeSpan.FromDays(10);
        NLog.Logger Logger { get; set; }

        public BackTester(MultiMarketSimulator simulator, TraderBot bot)
        {
            Simulator = simulator;
            Bots = new[] { bot };
        }
        public BackTester(MultiMarketSimulator simulator, TraderBot[] bots)
        {
            Simulator = simulator;
            Bots = bots;
        }

        public void Start()
        {
            Logger = NLog.LogManager.GetLogger("BackTester");
            Logger.Info($"Starting backtest {StartTime} - {EndTime}");
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (Started)
                return;
            Started = true;

            foreach (var Bot in Bots)
                Bot.Start(true).Wait();

            int steps = 1;
            decimal BalancePeak = 0;


            var startingBal = -1m;
            while (Simulator.NextTick() && Simulator.Time < EndTime)
            {
                if (startingBal < 0)
                    startingBal = Simulator.GetEquity(BaseAsset);
                foreach (var Bot in Bots)
                    Bot.OnTickAsync().Wait();
                steps++;

                var balance = Simulator.GetEquity(BaseAsset);
                EquityHistory.Add((Simulator.Time, balance));
                BalancePeak = balance > BalancePeak ? balance : BalancePeak;
                if (BalancePeak - balance > MaxDrowDown)
                {
                    MaxDrowDown = BalancePeak - balance;
                }
            }

            var totalBal = Simulator.GetEquity(BaseAsset);
            //get the total fee paid calculated on commission asset
            var feeList = Simulator.Trades.Select(
                                                tr =>
                                                {
                                                    if (tr.Symbol.EndsWith(tr.CommissionAsset))
                                                        return tr.Commission;
                                                    else
                                                        return tr.Commission * tr.Price;
                                                });
            var lostInFee = feeList.Sum();
            //get profit
            var profit = totalBal - startingBal;
            var totalBuys = Simulator.Trades.Where(tr => tr.Direction == TradeDirection.Buy).Count();
            sw.Stop();
            Logger.Info($"Test terminated in {sw.ElapsedMilliseconds} ms.");
            Logger.Info($"Balance: {totalBal} - Trades:{Simulator.Trades.Count()} - Lost in fee:{lostInFee}");
            if (totalBuys > 0)
                Logger.Info($"Profit/buy: {(totalBal - startingBal) / totalBuys:F8} - MaxDrawDown:{MaxDrowDown} - Profit/MDD:{profit / MaxDrowDown}");

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

        public void Start(string sessionName = "unnamed")
        {
            Logger = NLog.LogManager.GetLogger("Optimizer");
            Logger.Info($"Starting optimization session: {sessionName}");
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
                Logger.Info($"--------------- {bot.ToString()} -------------------" + msg);

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
