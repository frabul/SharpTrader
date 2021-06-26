using Newtonsoft.Json;
using SharpTrader.AlgoFramework;
using SharpTrader.Core.BrokersApi.Binance;
using SharpTrader.Indicators;
using SharpTrader.MarketSimulator;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    class ChartsTest
    {
        DateTime startTime = new DateTime(2020, 06, 01);
        DateTime endTime = new DateTime(2021, 06, 01);
        public BinanceTradeBarsRepository HostoryDb { get; private set; }
        public SymbolHistoryId DbSymbol { get; private set; }

        public async Task StartupAsync()
        {
            HostoryDb = new BinanceTradeBarsRepository(@"C:\projects\temp\Data3", ChunkFileVersion.V3, ChunkSpan.OneDay);
            DbSymbol = new SymbolHistoryId("Binance", "ETHBTC", TimeSpan.FromSeconds(60));

            await HostoryDb.AssureData(DbSymbol, startTime, startTime.AddDays(20));
            HostoryDb.SaveAndClose(DbSymbol);

        }

        public void TestSmall()
        {
            var data = HostoryDb.GetSymbolHistory(DbSymbol, startTime, startTime.AddDays(15));

            SharpTrader.Charts.Chart chart = new Charts.Chart(); 
            var mainFigure = chart.NewFigure();
            mainFigure.HeightRelative = 2;

            List<ITradeBar> bars = new List<ITradeBar>();
            var aggregator = new TradeBarConsolidator(TimeSpan.FromMinutes(10));
            aggregator.OnConsolidated += bar => bars.Add(bar);
            foreach (var c in data.Ticks)
                aggregator.Update(c);

            mainFigure.PlotCandlesticks("ETHBTC", data.Ticks);
             
            var op = new Operation("123", new Signal(), null, OperationType.SellThenBuy);
            var tick = data.Ticks[35];
            op.AddTrade(new Trade(data.Market, data.Symbol, tick.Time.AddSeconds(-30), TradeDirection.Buy, (decimal)tick.Low, 10, null));
            tick = data.Ticks[105];
            op.AddTrade(new Trade(data.Market, data.Symbol, tick.Time.AddSeconds(-30), TradeDirection.Sell, (decimal)tick.Low, 10, null)); 
            mainFigure.PlotOperation(op);

            List<IBaseData> l1 = new List<IBaseData>();
            List<IBaseData> l2 = new List<IBaseData>();
            List<IBaseData> l3 = new List<IBaseData>();

            var filter1 = new SharpTrader.Indicators.NormalizeToCurrentValue("hp1n", new HighPass<IBaseData>("hp1", 15));
            var filter2 = new SharpTrader.Indicators.NormalizeToCurrentValue("hp2n", new HighPass<IBaseData>("hp2", 30));
            var filter3 = new SharpTrader.Indicators.NormalizeToCurrentValue("hp3n", new HighPass<IBaseData>("hp1", 120));

            filter1.Updated += (source, rec) => l1.Add(rec);
            filter2.Updated += (source, rec) => l2.Add(rec);
            filter3.Updated += (source, rec) => l3.Add(rec);

            while (data.Ticks.MoveNext())
            { 
                filter1.Update(data.Ticks.Current);
                filter2.Update(data.Ticks.Current);
                filter3.Update(data.Ticks.Current);
            }

            var figure = chart.NewFigure();

            figure.HeightRelative = 1;
            figure.PlotLine("hp15", l1, ARGBColors.BlueViolet);
            figure.PlotLine("hp30", l2, ARGBColors.Red);
            figure.PlotLine("hp120", l3, ARGBColors.Green);

            //mainFigure.PlotLine(l3, ARGBColors.CornflowerBlue, axis: "left");

            figure.AddMarker(startTime.AddHours(10), ARGBColors.CadetBlue, Charts.SeriesMarkerPosition.aboveBar);
            figure.AddMarker(startTime.AddHours(10).AddMinutes(1), ARGBColors.CadetBlue, Charts.SeriesMarkerPosition.belowBar);
            figure.AddMarker(startTime.AddHours(10).AddMinutes(2.5), ARGBColors.CadetBlue, Charts.SeriesMarkerPosition.aboveBar);

            chart.Serialize(@"D:\ProgettiBck\SharpTraderBots\SharpTrader\SharpTrader.Core\Plotting\dist\chart.json");
            var str = JsonConvert.SerializeObject(chart);
        }

        public static async Task Run()
        {
            var test = new ChartsTest();
            await test.StartupAsync();
            test.TestSmall();
        }
    }
}
