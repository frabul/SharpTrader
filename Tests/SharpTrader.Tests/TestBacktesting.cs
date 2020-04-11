using SharpTrader.Plotting;
using SharpTrader.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestBacktesting
    {
        static readonly string DataDir = ".\\Data\\";
        static readonly string BaseAsset = "BTC";
        static readonly string TradeAsset = "ETH";
        static string Symbol => TradeAsset + BaseAsset;
        public static void Run()
        {
            //download ETHBTC history data
            var downloader = new BinanceDataDownloader(DataDir, 0.6d) { ConcurrencyCount = 4 };
            downloader.DownloadHistoryAsync(Symbol, new DateTime(2018, 1, 1), TimeSpan.FromDays(1)).Wait();
            downloader.SynchSymbolsTable(DataDir); //this will create a symbols list file for the binance market simulator
            //open history datablase 
            var historyDB = new HistoricalRateDataBase(DataDir);
            //create a market simulator - we create configuration here ( or should add a configuration file in DataDir
            var marketsSimulatorConfig = new MultiMarketSimulator.Configuration()
            {
                Markets = new MultiMarketSimulator.MarketConfiguration[]
                 {
                      new MultiMarketSimulator.MarketConfiguration()
                      {
                           MarketName = "Binance",
                           MakerFee = 0.001,
                           TakerFee = 0.001, 
                      }
                 }
            };
            MultiMarketSimulator simulator = new MultiMarketSimulator(DataDir, marketsSimulatorConfig, historyDB);
            
            //get binance market api
            var api = simulator.GetMarketApi("Binance");
            
            //simulation start 
            var simStart = new DateTime(2018, 1, 1);
            var simEnd = new DateTime(2019, 03, 1);
           
            simulator.Deposit("Binance", BaseAsset, 100);
            var theBots = new TraderBot[] { new TurtleBuyBot(api, Symbol) };
            BackTester tester = new BackTester(simulator, theBots)
            {
                BaseAsset = BaseAsset,
                StartTime = simStart,
                EndTime = simEnd, 
            };
            tester.Start();

            foreach (var bot in theBots)
            {
                var vm = TraderBotResultsPlotViewModel.RunWindow(bot.Drawer);
                vm.UpdateChart();
            }

            //----- plot equity line -----
            PlotHelper helper = new PlotHelper();
            int cnt = 0;
            helper.PlotLine(
                tester.EquityHistory.Where(e => cnt++ % 50 == 0).Select(e => (e.time, (double)e.bal)).ToList(),
                new ColorARGB(255, 125, 11, 220));
            var viewModel = TraderBotResultsPlotViewModel.RunWindow(helper);
            viewModel.UpdateChart();
            Console.ReadLine();

        }

     
    }
    /// <summary>
    /// Simple bot that follows some kind of turtle entry strategy ( only buy )
    /// </summary>
    public class TurtleBuyBot : TraderBot
    {
        private ISymbolFeed Feed;
        private TimeSerieNavigator<ITradeBar> Chart;
        private Indicators.Max<ITradeBar> Max;
        private Indicators.Min<ITradeBar> Min;
        private decimal AmountHold = 0;
        public IMarketApi Market { get; set; }
        public string Symbol { get; private set; }

        /// <summary>
        /// Candles timespan ( chart timeframe )
        /// </summary>
        public TimeSpan TimeFrame { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// we buy when price breaks  out over the maximum price in last MaxSteps hours
        /// </summary>
        public int MaxSteps { get; set; } = 160;

        /// <summary>
        /// we sell when price breask below the mimum prince of last MinSteps hours
        /// </summary>
        public int MinSteps { get; set; } = 80;  
    
        public decimal TradeAmount { get; set; } = 1; 
        public decimal StopLoss { get; set; } = 0.03m;
         
        public TurtleBuyBot(IMarketApi market, string symbol)
        {
            Market = market;
            this.Symbol = symbol;
        }

        /// <summary>
        /// This is the method that is going to be called when the bot is started
        /// </summary>
        /// <returns></returns>
        public override async Task OnStartAsync()
        {
            Feed = await Market.GetSymbolFeedAsync(Symbol);
            Chart = await Feed.GetNavigatorAsync(TimeFrame);
            Min = new Indicators.Min<ICandlestick>(Chart, c => c.Low, MinSteps);
            Max = new Indicators.Max<ICandlestick>(Chart, c => c.High, MaxSteps);

            //set the candles that are going to be displayed in the chart
            Drawer.Candles = new TimeSerieNavigator<ICandlestick>(Chart);
            // plot the minimum and maximum price
            Drawer.PlotLines(Min.GetNavigator(), new ColorARGB(255, 255, 0, 0), v => new double[] { v.Value });
            Drawer.PlotLines(Max.GetNavigator(), new ColorARGB(255, 0, 0, 255), v => new double[] { v.Value });
        }

        ITrade Lastbuy;
        public override async Task OnTickAsync()
        {   
            //if the indicators are ready the bot can run
            if (Min.IsReady && Max.IsReady)
            { 
                if (AmountHold == 0 && Feed.Ask > Max[0].Value)
                {
                    //if we didn't bought already and ask price is is higher than the maximum price in the last MaxSteps candles
                    var res = await Market.MarketOrderAsync(Symbol, TradeType.Buy, TradeAmount);
                    Debug.Assert(res.Status == MarketOperationStatus.Completed);
                    var trade = Market.Trades.First(t => t.OrderId == res.Result.Id);
                    AmountHold += trade.Amount - trade.Fee / trade.Price; 
                    Lastbuy = trade;
                }
                else if (AmountHold > 0)
                {
                    var stoploss = Lastbuy.Price * (1 - StopLoss);
                    if (Feed.Ask < Math.Max((double)stoploss, Min[0].Value))
                    { 
                        var res = await Market.MarketOrderAsync(Symbol, TradeType.Sell, AmountHold);
                        Debug.Assert(res.Status == MarketOperationStatus.Completed);
                        AmountHold = 0;
                        var sellTrade = Market.Trades.First(t => t.OrderId == res.Result.Id);
                        //plot the operation
                        Drawer.PlotOperation(Lastbuy, sellTrade);
                    }
                }
            }
        }
    }

}
