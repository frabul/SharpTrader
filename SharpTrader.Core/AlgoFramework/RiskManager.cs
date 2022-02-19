using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class RiskManager : IObjectSerializationProvider
    {
        public RiskManager()
        {

        }
        public TradingAlgo Algo { get; private set; }
        protected abstract Task OnInitialize();
        public virtual void OnSymbolsChanged(SelectedSymbolsChanges changes) { }
        public abstract Task Update(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        } 

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
