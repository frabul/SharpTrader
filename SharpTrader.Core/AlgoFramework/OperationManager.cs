using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class OperationManager : IObjectSerializationProvider
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
        
        public abstract decimal GetInvestedOrLockedAmount(ISymbolInfo symbol, string asset);
           
        public virtual void RegisterCustomSerializers(BsonMapper mapper) { }

        /// <summary>
        /// Gets a state object that should be saved and restored on restarts
        /// </summary> 
        public virtual object GetState() { return new object(); }
        /// <summary>
        /// Restorse the statate that had been saved ( taken with GetState)
        /// </summary> 
        public virtual void RestoreState(object state) { }
    }
}
