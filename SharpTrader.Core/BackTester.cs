using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpTrader.AlgoFramework;
using SharpTrader.Indicators;
using SharpTrader.MarketSimulator;
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
        public class Configuration
        {
            public string SessionName = "Backetester";
            public string DataDir { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public string HistoryDb { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public bool PlottingEnabled = false;
            public bool PlotResults = true;
            public string AlgoClass;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }

            public AssetAmount StartingBalance { get; set; } = new AssetAmount("BTC", 100);
            public string Market = "Binance";

            public JObject AlgoConfig = new JObject();
        }

        private Configuration Config { get; }

        private MultiMarketSimulator MarketSimulator;
        private object algoConfig;

        public TradingAlgo Algo { get; private set; }
        public bool Started { get; private set; }
        public DateTime StartTime => Config.StartTime;
        public DateTime EndTime => Config.EndTime;
        public string BaseAsset => Config.StartingBalance.Asset;
        public List<(DateTime time, decimal bal)> EquityHistory { get; set; } = new List<(DateTime time, decimal bal)>();
        public decimal FinalBalance { get; set; }
        public decimal MaxDrowDown { get; private set; }
        /// <summary>
        /// How much history should be loaded back from the simulation start time
        /// </summary>
        public TimeSpan HistoryLookBack { get; private set; } = TimeSpan.Zero;
        public NLog.Logger Logger { get; set; }
        public Action<PlotHelper> ShowPlotCallback { get; set; }

        public BackTester(Configuration config)
        {
            Config = config;

            if (Logger == null)
                Logger = NLog.LogManager.GetLogger("BackTester_" + Config.SessionName);

            var HistoryDB = new HistoricalRateDataBase(Config.HistoryDb);
            this.MarketSimulator = new MultiMarketSimulator(Config.DataDir, HistoryDB, Config.StartTime, Config.EndTime);
            MarketSimulator.Deposit(Config.Market, Config.StartingBalance.Asset, Config.StartingBalance.Amount);

            var algoClass = Type.GetType(Config.AlgoClass);
            if (algoClass == null)
                throw new Exception($"Algorith class {Config.AlgoClass} not found.");

            var ctors = algoClass.GetConstructors();
            var myctor = ctors.FirstOrDefault(ct =>
                                                {
                                                    var pars = ct.GetParameters();
                                                    return pars.Length == 2 && pars[0].ParameterType == typeof(IMarketApi);
                                                });
            if (myctor == null)
                throw new Exception("Unable to find a constructor with 2 parameters (ImarketApi, config)");
            var configClass = myctor.GetParameters()[1].ParameterType;
            algoConfig = null;
            try
            {
                algoConfig = JsonConvert.DeserializeObject(  config.AlgoConfig.ToString(), configClass);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to translate from provided algo config to ${configClass.FullName }: ${ex.Message}");
            }

            this.Algo = myctor.Invoke(new[] { MarketSimulator.GetMarketApi(config.Market), algoConfig }) as TradingAlgo;
            this.Algo.IsPlottingEnabled = Config.PlottingEnabled;
            this.Algo.ShowPlotCallback = (pl) => this?.ShowPlotCallback(pl);
            if (this.Algo == null)
                throw new Exception("Wrong algo class");
        }

        public void Start()
        {
            if (Started)
                return;
            Logger.Info($"Starting backtest {StartTime} - {EndTime}");
            Logger.Info($"Backtesting {Algo.ToString()}, configuration:\n" + JObject.FromObject(algoConfig).ToString());

            Stopwatch sw = new Stopwatch();
            sw.Start();

            Started = true;

            this.Algo.Start(true).Wait();

            int steps = 1;
            decimal BalancePeak = 0;


            var startingBal = -1m;
            while (MarketSimulator.NextTick() && MarketSimulator.Time < EndTime)
            {
                if (startingBal < 0)
                    startingBal = MarketSimulator.GetEquity(BaseAsset);

                Algo.OnTickAsync().Wait();
                steps++;

                var balance = MarketSimulator.GetEquity(BaseAsset);
                EquityHistory.Add((MarketSimulator.Time, balance));
                BalancePeak = balance > BalancePeak ? balance : BalancePeak;
                if (BalancePeak - balance > MaxDrowDown)
                {
                    MaxDrowDown = BalancePeak - balance;
                }
            }

            var totalBal = MarketSimulator.GetEquity(BaseAsset);
            //get the total fee paid calculated on commission asset
            var feeList = MarketSimulator.Trades.Select(
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
            var totalBuys = MarketSimulator.Trades.Where(tr => tr.Direction == TradeDirection.Buy).Count();
            sw.Stop();
            Logger.Info($"Test terminated in {sw.ElapsedMilliseconds} ms.");
            Logger.Info($"Balance: {totalBal} - Trades:{MarketSimulator.Trades.Count()} - Lost in fee:{lostInFee}");
            if (totalBuys > 0)
                Logger.Info($"Profit/buy: {(totalBal - startingBal) / totalBuys:F8} - MaxDrawDown:{MaxDrowDown} - Profit/MDD:{profit / MaxDrowDown}");

            foreach (var p in Algo.Plots)
                ShowPlotCallback?.Invoke(p);

            if (Config.PlotResults)
            {
                PlotHelper plot = new PlotHelper("Equity");
                var points = EquityHistory.Select(e => new IndicatorDataPoint(e.time, (double)e.bal));
                plot.PlotLine(points, ARGBColors.Purple);
                plot.InitialView = (Config.StartTime, Config.EndTime);
                ShowPlotCallback?.Invoke(plot);
            }
        }
    }


}
