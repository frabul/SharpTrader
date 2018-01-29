using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public abstract class TraderBot : IChartDataListener
    {
        private List<object[]> OptimizationSpace = new List<object[]>();
        private List<int> OptimizationIndexes;

        public bool Active { get; set; }
        public IMarketsManager MarketsManager { get; }
        public IMarketApi Market { get; set; }
        public GraphDrawer Drawer { get; }

        public bool Started { get; private set; }

        public int[] OptimizationArray
        {
            get => OptimizationIndexes.ToArray();
            set => OptimizationIndexes = value.ToList();
        }

        public TraderBot(IMarketsManager marketsManager)
        {
            Drawer = new GraphDrawer();
            MarketsManager = marketsManager;
        }

        public TraderBot(IMarketApi market)
        {
            Drawer = new GraphDrawer();
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

        protected object Optimize(object[] values)
        {
            if (Started)
                throw new InvalidOperationException("Optimize(..) should only be during OnStart or constructor");
            OptimizationSpace.Add(values);
            if (OptimizationIndexes.Count < OptimizationSpace.Count)
            {
                OptimizationIndexes.Add(0);
            }
            return OptimizationSpace[OptimizationIndexes[OptimizationSpace.Count - 1]];
        }
    }


}
