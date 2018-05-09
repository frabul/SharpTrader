﻿using System;
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


        public override void OnStart()
        {
            Feed = Market.GetSymbolFeedAsync("BCPTETH").Result;
            var feedNav = Feed.GetNavigatorAsync(TimeSpan.FromMinutes(15)).Result;
            Indicators.ZeroLagMA indi = new Indicators.ZeroLagMA(25, feedNav);

            Drawer.Candles = feedNav;
            var color = new ColorARGB(255, 110, 200, 160);

            Drawer.PlotLines(indi.GetNavigator(), color, e => new[] { e.ZMA, e.ZMA + 1.5 * e.StdDev, e.ZMA - 1.5 * e.StdDev }, false);
        }
    }
}
