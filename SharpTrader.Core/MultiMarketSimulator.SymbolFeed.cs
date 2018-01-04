using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        class SymbolFeed : ISymbolFeed
        {
            public SymbolFeed()
            {

            }

            public event Action<ISymbolFeed> NewCandle;
            public event Action<ISymbolFeed> OnTick;
            public double Ask { get; set; }
            public double Bid { get; set; }

            public string Market { get; set; }

            public double Spread { get; set; }

            public string Symbol { get; set; }

            public double Volume24H { get; set; }
            public Candle[] GetChartData(TimeSpan timeframe)
            {
                throw new NotImplementedException();
            }
        }
    }
}
