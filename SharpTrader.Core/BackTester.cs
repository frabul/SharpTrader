using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpTrader.AlgoFramework;
using SharpTrader.Charts;
using SharpTrader.Indicators;
using SharpTrader.MarketSimulator;
using SharpTrader.Storage;
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
        public class Statistics
        {
            public List<IndicatorDataPoint> EquityHistory { get; set; } = new List<IndicatorDataPoint>();
            public double BalancePeak { get; private set; }
            public double MaxDrowDown { get; private set; }
            public double StartingEquity { get; private set; }
            public double Profit { get; private set; }
            public double ProfitOverMaxDrowDown { get; private set; }
            private bool IsFirstRecord = true;
            internal void Update(DateTime time, double equity)
            {
                if (IsFirstRecord)
                    StartingEquity = equity;

                IsFirstRecord = false;
                EquityHistory.Add(new IndicatorDataPoint(time, equity));
                BalancePeak = equity > BalancePeak ? equity : BalancePeak;
                if (BalancePeak - equity > MaxDrowDown)
                    MaxDrowDown = BalancePeak - equity;

                Profit = equity - StartingEquity;
                if (MaxDrowDown != 0)
                    ProfitOverMaxDrowDown = Profit / MaxDrowDown;
                else
                    ProfitOverMaxDrowDown = float.PositiveInfinity;
            }
        }

        public class Configuration
        {
            public string SessionName = "Backetester";
            public string DataDir { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public string HistoryDb { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public string LogDir { get; } = $"./logs/";
            public bool PlottingEnabled { get; set; } = false;
            public bool PlotResults { get; set; } = false;
            public string AlgoClass;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public bool IncrementalHistoryLoading { get; set; } = false;
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


        Dictionary<string, double> LastPrices = new Dictionary<string, double>();
        public Statistics BotStats;
        public Statistics BenchmarkStats;

        /// <summary>
        /// How much history should be loaded back from the simulation start time
        /// </summary>
        public TimeSpan HistoryLookBack { get; private set; } = TimeSpan.Zero;
        public NLog.Logger Logger { get; set; }
        public Action<PlotHelper> ShowPlotCallback { get; set; }

        public BackTester(Configuration config) : this(config, new TradeBarsRepository(config.HistoryDb))
        {

        }
        public BackTester(Configuration config, TradeBarsRepository db)
        {
            Config = config;

            if (Logger == null)
                Logger = NLog.LogManager.GetLogger("BackTester_" + Config.SessionName);

            var HistoryDB = db;
            this.MarketSimulator = new MultiMarketSimulator(Config.DataDir, HistoryDB, Config.StartTime, Config.EndTime);
            this.MarketSimulator.IncrementalHistoryLoading = config.IncrementalHistoryLoading;
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
                algoConfig = JsonConvert.DeserializeObject(config.AlgoConfig.ToString(), configClass);
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
            this.BenchmarkStats = new Statistics();
            this.BotStats = new Statistics();
            Logger.Info($"Backtesting {Algo.Version} from  {StartTime} - {EndTime} with configuration:\n" + JObject.FromObject(algoConfig).ToString());

            Stopwatch sw = new Stopwatch();
            sw.Start();

            Started = true;

            this.Algo.Start(true).Wait();
            this.Algo.RequestResumeEntries();
            int steps = 1;


            var startingBal = -1m;
            double currentBenchmarkVal = (double)Config.StartingBalance.Amount;
            var oldCursor = Console.CursorTop;
            while (MarketSimulator.NextTick() && MarketSimulator.Time < EndTime)
            {
                if (startingBal < 0)
                    startingBal = MarketSimulator.GetEquity(BaseAsset);

                Algo.OnTickAsync().Wait();
                steps++;
                //---- update equity and benchmark ---------- 
                if (steps % 60 == 0)
                {
                    var balance = MarketSimulator.GetEquity(BaseAsset);
                    BotStats.Update(MarketSimulator.Time, (double)balance);
                    //--- calculate benchmark change
                    double changeSum = 0;
                    int changeCount = 0;
                    foreach (var symData in Algo.SymbolsData.Values)
                    {
                        if (symData.Feed != null && symData.Feed.Ask > 0 && symData.Feed.Bid > 0)
                        {
                            var sk = symData.Feed.Symbol.Key;
                            if (symData.Feed != null && LastPrices.ContainsKey(sk))
                            {
                                changeCount++;
                                var inc = (symData.Feed.Bid - LastPrices[sk]) / LastPrices[sk];
                                if (inc > 5)
                                    Logger.Info($"Anomalous increase {inc} for symbol {symData.Feed.Symbol.Key}");
                                changeSum += inc;

                            }
                            LastPrices[sk] = symData.Feed.Ask;
                        }
                    }
                    if (changeCount > 0)
                    {
                        currentBenchmarkVal += changeSum * (double)currentBenchmarkVal / changeCount;
                        BenchmarkStats.Update(MarketSimulator.Time, currentBenchmarkVal);
                    }

                }

                if (steps % 240 == 0)
                {
                    var prcDone = (double)(MarketSimulator.Time - StartTime).Ticks / (EndTime - StartTime).Ticks * 100;
                    if (oldCursor == Console.CursorTop)
                        Console.CursorTop = oldCursor - 1;

                    Console.WriteLine($"Simulation time: {MarketSimulator.Time} - {prcDone:f2}% completed");
                    oldCursor = Console.CursorTop;
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

            sw.Stop();

            var operations = Algo.ActiveOperations.Concat(Algo.ClosedOperations).Where(o => o.AmountInvested > 0).ToList();

            Logger.Info($"Test terminated in {sw.ElapsedMilliseconds} ms.");
            Logger.Info($"Balance: {totalBal} - Operations:{operations.Count} - Lost in fee:{lostInFee}");
            if (operations.Count > 0)
            {
                Logger.Info($"Algorithm => Profit: {BotStats.Profit:F4} - Profit/MDD: {BotStats.ProfitOverMaxDrowDown:F3} - Profit/oper: {BotStats.Profit / operations.Count:F8} ");
                Logger.Info($"BenchMark => Profit: {BenchmarkStats.Profit:F4} - Profit/MDD: {BenchmarkStats.ProfitOverMaxDrowDown:F3}  ");
            }

            foreach (var p in Algo.Plots)
                ShowPlotCallback?.Invoke(p);

            if (Config.PlotResults)
            {
                Chart chart = new Chart(this.Config.SessionName + " Equity");
                var plot = chart.NewFigure();
                int skipCount = (BotStats.EquityHistory.Count + 2000) / 2000;
                List<IndicatorDataPoint> botPoints = new List<IndicatorDataPoint>();
                List<IndicatorDataPoint> benchPoints = new List<IndicatorDataPoint>();

                for (int i = 0; i < BotStats.EquityHistory.Count; i += skipCount)
                {
                    var eqPoint = BotStats.EquityHistory[i];
                    botPoints.Add(new IndicatorDataPoint(eqPoint.Time, eqPoint.Value));
                    //--- add bench point ---
                    var bpIndex = BenchmarkStats.EquityHistory.BinarySearch(eqPoint, CandlestickTimeComparer.Instance);
                    if (bpIndex >= 0)
                    {
                        var benchPoint = BenchmarkStats.EquityHistory[bpIndex];
                        benchPoints.Add(benchPoint);
                    }
                }

                plot.PlotLine("Equity", botPoints, ARGBColors.Blue);
                plot.PlotLine("Benchmark", benchPoints, ARGBColors.MediumPurple);

                Directory.CreateDirectory(Config.LogDir);
                chart.Serialize(Path.Combine(Config.LogDir, $"{Config.SessionName}_Chart_equity.json"));
            }
        }
    }


}
