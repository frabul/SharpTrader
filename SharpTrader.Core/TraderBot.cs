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
        private List<PlotHelper> _Plots = new List<PlotHelper>();
        private volatile bool _started;
        public IReadOnlyList<PlotHelper> Plots => _Plots;

        public void AddPlot(PlotHelper plot)
        {
            if (!_Plots.Contains(plot))
                _Plots.Add(plot);
        }

        public bool BackTesting { get; private set; }
        public bool Active { get; set; }
        public bool Started { get { Thread.MemoryBarrier(); return _started; } private set => _started = value; }
        public async Task Start(bool backtesting)
        {
            Started = false;
            BackTesting = backtesting;
            await OnStartAsync();
            Started = true;
        }

        public abstract Task OnStartAsync();
        public abstract Task OnTickAsync();
    }


}
