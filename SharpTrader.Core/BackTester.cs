using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        public NLog.Logger Logger { get; set; }

        public BackTester(  MultiMarketSimulator simulator, TraderBot bot)
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
            if (Logger == null)
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

    class OptimizerSession
    {
        public int SpaceSize { get; set; }
        public int Lastexecuted { get; set; }
    }

    public class Optimizer
    {
        string SessionFile => $"OptimizerSession_{SessionName}.json";
        public string SessionName { get; private set; }
        OptimizerSession Session;
        public Func<IMarketsManager, object, TraderBot> BotFactory { get; set; }
        public Func<MultiMarketSimulator> MarketFactory { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }


        public string BaseAsset { get; set; }
        private NLog.Logger Logger;

        private OptimizationSpace2 BaseSpace;

        public Optimizer(OptimizationSpace2 optimizationSpace)
        {
            BaseSpace = optimizationSpace;
        }

        public void Start(string sessionName = "unnamed")
        {
            SessionName = sessionName;

            BaseSpace.Initialize();
            Session = new OptimizerSession()
            {
                Lastexecuted = -1,
                SpaceSize = BaseSpace.Configurations.Count
            };
            if (File.Exists(SessionFile))
            {
                var text = File.ReadAllText(SessionFile);
                var loaded = JsonConvert.DeserializeObject<OptimizerSession>(text);
                if (loaded.SpaceSize == Session.SpaceSize)
                    Session.Lastexecuted = loaded.Lastexecuted;
            }

            Logger = NLog.LogManager.GetLogger($"Optimizer_{sessionName}");
            Logger.Info($"Starting optimization session: {sessionName}");

            for (int i = Session.Lastexecuted + 1; i < BaseSpace.Configurations.Count; i++)
            {
                RunOne(BaseSpace.Configurations[i]);
                Session.Lastexecuted = i;
                File.WriteAllText(SessionFile, JsonConvert.SerializeObject(Session));
            }

            //Parallel.ForEach(paramSets, new ParallelOptions { MaxDegreeOfParallelism = 2 }, act);
        }

        void RunOne(object config)
        {
            var sim = MarketFactory();
            var bot = BotFactory(sim, config);
            var startingEquity = sim.GetEquity(BaseAsset);
            var backTester = new BackTester(sim, bot)
            {
                StartTime = StartTime,
                EndTime = EndTime,
                BaseAsset = BaseAsset,
                Logger = this.Logger,
            };
            var obj = JsonConvert.SerializeObject(config, Formatting.Indented);
            Logger.Info($"--------------- {bot.ToString()} -------------------\n" + obj + "\n");

            backTester.Start();
            //collect info  
        }
    }
}
