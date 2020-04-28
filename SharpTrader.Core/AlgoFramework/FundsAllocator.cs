using System.Collections.Generic;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class FundsAllocator
    {
        public Dictionary<string, List<Operation>> Operations { get; private set; } = new Dictionary<string, List<Operation>>();
        public TradingAlgo Algo { get; private set; }
        protected virtual Task OnInitialize() { return Task.CompletedTask; }
        public virtual void OnSymbolsChanged(SelectedSymbolsChanges changes) { }

        public abstract void Update(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        }
    }

}
