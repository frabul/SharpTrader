using LiteDB;
using Newtonsoft.Json.Linq;
using System;
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

        public virtual BsonDocument GetSerializationData(Signal signal)
        {
            return BsonMapper.Global.ToDocument<Signal>(signal);
        }

        public virtual Signal DeserializeSignal(BsonDocument doc)
        {

            return BsonMapper.Global.Deserialize<Signal>(doc);
        }

        public abstract void RegisterSerializationMappers(BsonMapper mapper);
    }
}
