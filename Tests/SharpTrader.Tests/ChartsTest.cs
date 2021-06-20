using Newtonsoft.Json;
using SharpTrader.Core.BrokersApi.Binance;
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
            HostoryDb = new BinanceTradeBarsRepository(@"C:\projects\temp\Data3", ChunkFileVersion.V3, ChunkSpan.OneMonth);
            DbSymbol = new SymbolHistoryId("Binance", "ETHBTC", TimeSpan.FromSeconds(60));

            await HostoryDb.AssureData(DbSymbol, startTime, endTime);
            HostoryDb.SaveAndClose(DbSymbol);

        }

        public void TestSmall()
        {
            var data = HostoryDb.GetSymbolHistory(DbSymbol, startTime, startTime.AddDays(10));
            
            SharpTrader.Drawing.Chart chart = new Drawing.Chart();

            chart.PlotCandlesticks("ETHBTC", data.Ticks);
            chart.Serialize("./chart.json");
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
