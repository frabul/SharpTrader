using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    class TestIndicatorsBot : TraderBot
    {
        private ISymbolFeed Feed;
        private IMarketApi Market;
        private ZeroLagMA Indicator;

        public TestIndicatorsBot(IMarketsManager marketsManager)
        {
            Market = marketsManager.GetMarketApi("Binance");
        }

    
        public override async Task OnStartAsync()
        {
            Feed = await Market.GetSymbolFeedAsync("BCPTETH"); 
            Indicator = new Indicators.ZeroLagMA(Feed.Symbol, 25 );
            Plot.Candles = await Feed.GetHistoryNavigator(new DateTime(2019,01,01 ));
            var color = new ColorARGB(255, 110, 200, 160);
            TimeSerie<ZeroLagMARecord> indicatorRecords = new TimeSerie<ZeroLagMARecord>();
            Indicator.Updated += (s, r) => indicatorRecords.AddRecord(r);
            Plot.PlotLines(indicatorRecords, color, e => new[] { e.ZMA, e.ZMA + 1.5 * e.StdDev, e.ZMA - 1.5 * e.StdDev }, false);
          
        }

     

        public override async Task OnTickAsync()
        {
         
        }
    }
}
