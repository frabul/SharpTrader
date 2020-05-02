using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    /// A module that generate trading signals  
    /// </summary>
    public abstract class Sentry : IObjectSerializationProvider
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

        public abstract void RegisterSerializationHandlers(BsonMapper mapper);

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
