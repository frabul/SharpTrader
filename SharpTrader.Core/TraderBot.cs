using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public abstract class TraderBot
    { 
        public bool Active { get; set; } 
        public PlotHelper Drawer { get; } = new PlotHelper(); 
        public bool Started { get; private set; } 
        public async Task Start()
        {
            await OnStart();
            Started = true;
        }

        public abstract Task OnStart();
        public abstract Task OnTick();
    }


}
