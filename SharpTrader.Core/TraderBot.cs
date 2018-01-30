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
        public PlotHelper Drawer { get; }

        public bool Started { get; private set; }

        public int[] OptimizationArray
        {
            get => OptimizationIndexes.ToArray();
            set => OptimizationIndexes = value.ToList();
        }

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

        public List<int[]> GetOptimizePermutations()
        {
            List<int[]> permutations = new List<int[]>();
            int[] currentPerm = new int[OptimizationSpace.Count];
            bool terminated = false;

            bool Increment(int i)
            {
                if (i == currentPerm.Length)
                    return false; //we created all possible permutations

                currentPerm[i] += 1;
                if (currentPerm[i] == OptimizationSpace[i].Length)
                {
                    currentPerm[i] = 0;
                    return Increment(i + 1);
                }
                else
                    return true;
            }

            while (Increment(0))
            {
                permutations.Add(currentPerm.ToArray());
            }
            return permutations;
        }
    }


}
