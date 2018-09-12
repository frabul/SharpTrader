using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader
{
    public abstract class TraderBot
    {
        private volatile bool _started;

        public bool BackTesting { get; private set; }
        public bool Active { get; set; }
        public PlotHelper Drawer { get; } = new PlotHelper();
        public bool Started { get { Thread.MemoryBarrier(); return _started; } private set => _started = value; }
        public async Task Start(bool backtesting)
        {
            Started = false;
            BackTesting = backtesting;
            await OnStartAsync();
            Started = true;
        }

        public abstract Task OnStartAsync();
        public abstract Task OnTick();
    }


}
