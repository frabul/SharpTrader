using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class OperationManager
    {
        public TradingAlgo Algo { get; private set; }
        protected virtual Task OnInitialize() { return Task.CompletedTask; }
        public virtual void OnSymbolsChanged(SelectedSymbolsChanges changes) { }
        public abstract Task Update(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        } 
        public abstract Task CancelAllOrders(Operation op); 
        public abstract Task CancelEntryOrders();
        internal abstract decimal GetInvestedOrLockedAmount(SymbolInfo symbol, string asset);
    }
}
