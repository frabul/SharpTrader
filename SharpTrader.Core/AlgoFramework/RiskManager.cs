using LiteDB;
using Newtonsoft.Json.Linq;
using System;
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

        internal virtual JToken GetSerializationData(object riskManagerData) { throw new NotImplementedException(); }
        internal virtual object DeserializeOperationData(JToken jToken) { throw new NotImplementedException(); }

        public abstract void RegisterSerializationMappers(BsonMapper mapper);
    }

}
