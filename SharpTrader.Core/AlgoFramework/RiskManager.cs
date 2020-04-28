using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class RiskManager
    {
        public TradingAlgo Algo { get; private set; }
        protected abstract Task OnInitialize();
        public virtual void OnSymbolsChanged(SelectedSymbolsChanges changes) { }
        public abstract Task Update(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        }
    }

}
