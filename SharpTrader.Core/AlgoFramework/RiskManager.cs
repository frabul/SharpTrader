using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class RiskManager : IObjectSerializationProvider
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

        internal virtual JToken GetSerializationData(object riskManagerData) { throw new NotImplementedException(); }
        internal virtual object DeserializeOperationData(JToken jToken) { throw new NotImplementedException(); }

        public abstract void RegisterSerializationHandlers(BsonMapper mapper);

        internal Task CancelAllOrders(Operation op)
        {
            throw new NotImplementedException();
        }

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
