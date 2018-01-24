using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    class BollBot : SharpTrader.TraderBot
    {


        public BollBot(IMarketsManager manager) : base(manager)
        {

        }

        public override void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle)
        {
            throw new NotImplementedException();
        }

        public override void Start()
        {
            throw new NotImplementedException();
        }
    }
}
