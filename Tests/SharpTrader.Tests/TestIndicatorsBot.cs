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

        public TestIndicatorsBot(IMarketsManager marketsManager) : base(marketsManager)
        {
            Market = MarketsManager.GetMarketApi("Binance");
        }

        public override void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle)
        {

        }

        public override void OnStart()
        {
            Feed = Market.GetSymbolFeed("XRPBTC");
            var feedNav = Feed.GetNavigator(TimeSpan.FromMinutes(5));
            Indicators.HighPass<ICandlestick> HighPass = new Indicators.HighPass<ICandlestick>(
                feedNav, c => c.Close, 20);

            Drawer.Candles = feedNav;
            var color = new ColorARGB(255, 110, 200, 160);
           
            Drawer.PlotLines(HighPass.GetNavigator(), color, e => new[] { e.Value }, true);
        }
    }
}
