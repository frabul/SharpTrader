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
            public decimal BalancePeak { get; private set; }
            public decimal MaxDrowDown { get; private set; }
            public decimal StartingEquity { get; private set; }
            public decimal Profit { get; private set; }
            public decimal ProfitOverMaxDrowDown { get; private set; }
            private bool IsFirstRecord = true;
            internal void Update(DateTime time, decimal equity)
            {
                if (IsFirstRecord)
                    StartingEquity = equity;

                IsFirstRecord = false;
                EquityHistory.Add(new IndicatorDataPoint(time, (double)equity));
                BalancePeak = equity > BalancePeak ? equity : BalancePeak;
                if (BalancePeak - equity > MaxDrowDown)
                    MaxDrowDown = BalancePeak - equity;

                Profit = equity - StartingEquity;
                if (MaxDrowDown != 0)
                    ProfitOverMaxDrowDown = Profit / MaxDrowDown;
                else
                    ProfitOverMaxDrowDown = decimal.MaxValue;
            }
        }

        public class Configuration
        {
            public string SessionName { get; set; } = "Backetester";
            public string DataDir { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public string HistoryDb { get; set; } = @"D:\ProgettiBck\SharpTraderBots\Bin\Data\";
            public string LogDir => Path.Combine(DataDir, "logs");
            public bool PlottingEnabled { get; set; } = false;
            public bool PlotResults { get; set; } = false;
            public string AlgoClass;
            public DateTime StartTime { get; set; }
            /// <summary>
            /// During this period ( from start to start + WarmUpTime) the algorithm will be interdicted from trading
            /// </summary>
            public TimeSpan WarmUpTime { get; set; }
            public DateTime EndTime { get; set; }
            public bool IncrementalHistoryLoading { get; set; } = false;
            public AssetAmount StartingBalance { get; set; } = new AssetAmount("BTC", 100);
            public string Market { get; set; } = "Binance";

            public JObject AlgoConfig { get; set; } = new JObject();

            public Configuration Clone()
            {
                var clone = this.MemberwiseClone() as Configuration;
                clone.AlgoConfig = (JObject)AlgoConfig.DeepClone();
                clone.StartingBalance = new AssetAmount(StartingBalance.Asset, StartingBalance.Amount);
                return clone;
            }
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
        public Serilog.ILogger Logger { get; private set; }
        public Action<PlotHelper> ShowPlotCallback { get; set; }

        public BackTester(Configuration config) : this(config, new TradeBarsRepository(config.HistoryDb), null, null)
        {

        }

        public BackTester(Configuration config, TradeBarsRepository db, Serilog.ILogger logger, Serilog.ILogger algoLogger)
        {
            Config = config;
            Logger = logger;
            if (Logger == null)
                Logger = Serilog.Log.Logger;

            Logger = Logger.ForContext<BackTester>()
                           .ForContext("BacktestSession", config.SessionName);
            if (algoLogger == null)
                algoLogger = Logger;
            else
                algoLogger = algoLogger.ForContext("BacktestSession", config.SessionName);

            var HistoryDB = db;
            this.MarketSimulator = new MultiMarketSimulator(Config.DataDir, HistoryDB, Config.StartTime, Config.EndTime);
            this.MarketSimulator.IncrementalHistoryLoading = config.IncrementalHistoryLoading;
            MarketSimulator.Deposit(Config.Market, Config.StartingBalance.Asset, Config.StartingBalance.Amount);

            var algoClass = Type.GetType(Config.AlgoClass);
            if (algoClass == null)
                throw new Exception($"Algorithm class {Config.AlgoClass} not found.");

            var ctors = algoClass.GetConstructors();
            var myctor = ctors.FirstOrDefault(ct =>
            {
                var pars = ct.GetParameters();
                return pars.Length == 3 && pars[0].ParameterType == typeof(IMarketApi);
            });
            if (myctor == null)
                throw new Exception("Unable to find a constructor with 3 parameters (ImarketApi, config)");
            var configClass = myctor.GetParameters()[2].ParameterType;
            algoConfig = null;
            try
            {
                algoConfig = JsonConvert.DeserializeObject(config.AlgoConfig.ToString(), configClass);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to translate from provided algo config to ${configClass.FullName}: ${ex.Message}");
            }


            this.Algo = myctor.Invoke(new[] { MarketSimulator.GetMarketApi(config.Market), algoLogger, algoConfig }) as TradingAlgo;
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
            Logger.ForContext("Config", Config, true)
                  .Information("Starting backtest {BacktestSession} from {StartTime:yyyy-MM-dd} to {EndTime:yyyy-MM-dd}.",
                               Config.SessionName,
                               StartTime + Config.WarmUpTime,
                               EndTime);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            Started = true;

            this.Algo.Start(true).Wait();
            this.Algo.RequestResumeEntries();
            int steps = 0;


            var startingBal = -1m;
            double currentBenchmarkVal = (double)Config.StartingBalance.Amount;

            int partialResultsLines = 0; // how many lines the partial results printed on console  
            while (MarketSimulator.NextTick() && MarketSimulator.Time < EndTime)
            {
                if (startingBal < 0)
                    startingBal = MarketSimulator.GetEquity(BaseAsset);
                bool warmUpCompleted = MarketSimulator.Time >= StartTime + Config.WarmUpTime;
                if (!warmUpCompleted)
                    Algo.RequestStopEntries().Wait();
                else
                    Algo.RequestResumeEntries();

                Algo.OnTickAsync().Wait();

                //---- update equity and benchmark ---------- 
                if (steps % 60 == 0 && warmUpCompleted)
                {
                    var balance = MarketSimulator.GetEquity(BaseAsset);
                    BotStats.Update(MarketSimulator.Time, balance);
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
                                {
                                    Logger.Warning("Anomalous increase {Inc} for symbol {Symbol} at {Time}",
                                                   inc,
                                                   symData.Feed.Symbol.Key,
                                                   symData.Feed.Time);
                                }
                                changeSum += inc;

                            }
                            LastPrices[sk] = symData.Feed.Ask;
                        }
                    }
                    if (changeCount > 0)
                    {
                        currentBenchmarkVal += changeSum * currentBenchmarkVal / changeCount;
                        BenchmarkStats.Update(MarketSimulator.Time, (decimal)currentBenchmarkVal);
                    }
                }

                //print partial results
                if (steps % (60 * 24) == 0)
                {
                    // delete partial results output on console 
                    Console.SetCursorPosition(0, Console.CursorTop - partialResultsLines);
                    var initialCursorPosition = Console.CursorTop;
                    // print partial results
                    var prcDone = (double)(MarketSimulator.Time - StartTime).Ticks / (EndTime - StartTime).Ticks * 100;
                    DeleteConsoleLines(1);
                    Console.WriteLine($"Simulation time: {MarketSimulator.Time} - {prcDone:f2}% completed");
                    PrintStats(true);
                    partialResultsLines = Console.CursorTop - initialCursorPosition;
                }

                steps++;
            }

            int pcount = 0;
            foreach (var plot in Algo.Plots)
            {
                ShowPlotCallback?.Invoke(plot);
                Chart chart = new Chart(this.Config.SessionName + "_plot" + pcount++);
                var fig = chart.NewFigure();
                fig.PlotCandlesticks("Candlesticks", plot.Candles);
                // add lines 
                int lineCount = 0;
                foreach(var line in plot.Lines)
                {
                    var points = line.Points.Select(p => new IndicatorDataPoint(p.X, p.Y));
                    fig.PlotLine("li" + lineCount++, points, line.Color, axis:line.AxisId);
                }
                // todo convertire tutto il resto
                chart.Serialize(Path.Combine(Config.LogDir, $"{chart.Name}_{DateTime.Now:yyyyMMddmmss}.json"));
            }

            //get profit 
            sw.Stop();
            Logger.Information("Test terminated in {Duration} ms.", sw.ElapsedMilliseconds);
            Console.WriteLine("Test terminated in {0} ms.", sw.ElapsedMilliseconds);
            PrintStats(false);

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
                chart.Serialize(Path.Combine(Config.LogDir, $"{Config.SessionName}_{DateTime.Now:yyyyMMddmmss}_Chart_equity.json"));
            }
        }

        private static void DeleteConsoleLines(int cnt)
        {
            var blankLine = new string(' ', Console.BufferWidth);
            for (int i = 0; i < cnt; i++)
                Console.WriteLine(blankLine);
            Console.CursorTop -= cnt;
        }

        private void PrintStats(bool consoleOnly)
        {
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
            var operations = Algo.ActiveOperations.Concat(Algo.ClosedOperations).Where(o => o.AmountInvested > 0).ToList();

            DeleteConsoleLines(2);
            Console.WriteLine(
                $"Time: {MarketSimulator.Time:yyyy/MM/dd hh:mm}\n" +
                $"Balance: {totalBal:F4} - Operations:{operations.Count} - Lost in fee:{lostInFee:F4}");

            var profitPerOper = operations.Count > 0 ? BotStats.Profit / operations.Count : 0;

            DeleteConsoleLines(2);
            if (operations.Count > 0)
            {
                Console.WriteLine($"Algorithm => Profit: {BotStats.Profit:F4} - Profit/MDD: {BotStats.ProfitOverMaxDrowDown:F3} - Profit/oper: {profitPerOper:F8} ");
                Console.WriteLine($"BenchMark => Profit: {BenchmarkStats.Profit:F4} - Profit/MDD: {BenchmarkStats.ProfitOverMaxDrowDown:F3}  ");
            }
            else
            {
                Console.WriteLine($"Algorithm => No data");
                Console.WriteLine($"BenchMark => Profit: {BenchmarkStats.Profit:F4} - Profit/MDD: {BenchmarkStats.ProfitOverMaxDrowDown:F3}  ");
            }

            if (!consoleOnly)
            {
                DeleteConsoleLines(4);
                Logger.Information("Backtest results:\n" +
                    "Balance: {totalBal:F4} - Operations:{OperationsCount} - Lost in fee:{lostInFee:F4}\n" +
                    "Algorithm => {@AlgoStats} \n" +
                    "BenchMark => {@BenchmarkStats}",
                    totalBal, operations.Count, lostInFee,
                    new { BotStats.StartingEquity, BotStats.Profit, BotStats.ProfitOverMaxDrowDown, ProfitPerOper = profitPerOper, BotStats.MaxDrowDown, BotStats.BalancePeak },
                    new { BenchmarkStats.StartingEquity, BenchmarkStats.Profit, BenchmarkStats.ProfitOverMaxDrowDown, BenchmarkStats.MaxDrowDown, BenchmarkStats.BalancePeak }
                    );
            }
        }
    }


}
