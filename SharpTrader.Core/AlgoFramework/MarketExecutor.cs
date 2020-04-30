using LiteDB;
using Newtonsoft.Json.Linq;
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
        
        public abstract decimal GetInvestedOrLockedAmount(SymbolInfo symbol, string asset);

        public virtual JToken SerializeOperationData(object executorData) { throw new NotImplementedException(); }
        public virtual object DeserializeOperationData(JToken jToken) { throw new NotImplementedException(); }

        public virtual JToken SerializeSymbolData(object executorData) { throw new NotImplementedException(); }
        public virtual object DeserializeSymbolData(JToken jToken) { throw new NotImplementedException(); }

        public abstract void RegisterSerializationMappers(BsonMapper mapper);
    }
}
