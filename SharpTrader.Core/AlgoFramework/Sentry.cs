using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    /// A module that generate trading signals  
    /// </summary>
    public abstract class Sentry
    {
        public TradingAlgo Algo { get; private set; }
        protected abstract Task OnInitialize();
        public abstract void OnSymbolsChanged(SelectedSymbolsChanges changes);

        public abstract void UpdateAsync(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        }
    } 
}
