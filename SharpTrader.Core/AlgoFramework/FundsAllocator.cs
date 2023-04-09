﻿using LiteDB;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class FundsAllocator : IObjectSerializationProvider
    {
        public Dictionary<string, List<Operation>> Operations { get; private set; } = new Dictionary<string, List<Operation>>();
        public TradingAlgo Algo { get; private set; }
        protected virtual Task OnInitialize() { return Task.CompletedTask; }
        public virtual void OnSymbolsChanged(SelectedSymbolsChanges changes) { }

        public abstract Task Update(TimeSlice slice);
        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        }

        /// <summary>
        /// Gets a state object that should be saved and restored on restarts
        /// </summary> 
        public virtual object GetState() { return new object(); }
        /// <summary>
        /// Restorse the statate that had been saved ( taken with GetState)
        /// </summary> 
        public virtual void RestoreState(object state) { }

        public virtual void RegisterCustomSerializers(BsonMapper mapper) { }
    }

}
