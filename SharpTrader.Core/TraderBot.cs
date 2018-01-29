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
        public GraphDrawer Drawer { get; }

        public bool Started { get; private set; }
        List<object[]> OptimizationSpace = new List<object[]>();
        List<int> OptimizationIndexes;

        public TraderBot(IMarketsManager marketApi)
        {
            Drawer = new GraphDrawer();
            MarketsManager = marketApi;
        }

        public abstract void OnStart();

        public void Start()
        {
            OnStart();
            Started = true;
        }

        public abstract void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle);

        protected object Optimize(object[] values)
        {
            if (Started)
                throw new InvalidOperationException("Optimize(..) can be called only during OnStart().");
            OptimizationSpace.Add(values);
            if (OptimizationIndexes.Count < OptimizationSpace.Count)
            {
                OptimizationIndexes.Add(0);
            }
            return OptimizationSpace[OptimizationIndexes[OptimizationSpace.Count -1]];
        }
    }


}
