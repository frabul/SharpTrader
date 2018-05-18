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

        public override Task OnStart()
        {
            Feed = Market.GetSymbolFeedAsync("BCPTETH").Result;
            var feedNav = Feed.GetNavigatorAsync(TimeSpan.FromMinutes(15)).Result;
            Indicator = new Indicators.ZeroLagMA(25, feedNav);
            Drawer.Candles = feedNav;
            var color = new ColorARGB(255, 110, 200, 160);
            Drawer.PlotLines(Indicator.GetNavigator(), color, e => new[] { e.ZMA, e.ZMA + 1.5 * e.StdDev, e.ZMA - 1.5 * e.StdDev }, false);
            return Task.CompletedTask;
        }

        public override async Task OnTick()
        {



        
  

           
        }
    }
}
