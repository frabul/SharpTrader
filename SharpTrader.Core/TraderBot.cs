using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core
{


    public abstract class TraderBot
    {
        public bool Active { get; set; }
        public IMarketApi Market { get;  }

        public TraderBot(IMarketApi marketApi)
        {
            Market = marketApi;
        }
        public abstract void Start();
    }


}
