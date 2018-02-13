using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public abstract class TraderBot : IChartDataListener
    {
    

        public bool Active { get; set; }
        public IMarketsManager MarketsManager { get; }
        public IMarketApi Market { get; set; }
        public PlotHelper Drawer { get; }

        public bool Started { get; private set; }

      

        public TraderBot(IMarketsManager marketsManager)
        {
            Drawer = new PlotHelper();
            MarketsManager = marketsManager;
        }

        public TraderBot(IMarketApi market)
        {
            Drawer = new PlotHelper();
            Market = market;
            Started = true;
        }

        public void Start()
        {
            OnStart();
            Started = true;
        }

        public abstract void OnStart();

        public abstract void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);

       

      
    }


}
