using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Dynamic;

namespace SharpTrader.AlgoFramework
{
    public class Signal
    { 
        public event Action<Signal> OnModify;

        public Signal(string Id, SymbolInfo symbol, SignalKind kind, DateTime creationTime)
        {
            Symbol = symbol;
            Kind = kind;
            CreationTime = creationTime;
        }

        /// <summary>
        /// Constructor dedicated to serialization
        /// </summary>
        [BsonCtor]
        internal Signal(string id)
        {
            Id = id;
        }
        public string Id { get; }

        /// <summary>
        /// The operation managing this signal ( if any )
        /// </summary>
        [BsonIgnore]
        public Operation Operation { get; set; }

        public SymbolInfo Symbol { get; private set; }
        public SignalKind Kind { get; private set; }
        public DateTime CreationTime { get; private set; }
        public decimal PriceTarget { get; private set; }

        /// <summary>
        /// If it's a buy operation this is the maximum price to buy
        /// If it's a sell operation this indicates the minimum price to sell
        /// </summary>
        public decimal PriceEntry { get; private set; }

        /// <summary>
        /// Any operation 
        /// </summary>
        public DateTime EntryExpiry { get; private set; }

        /// <summary>
        /// Every operation based on this signal is meant to be closed after this time
        /// </summary>
        public DateTime ExpireDate { get; private set; }

        public void ModifyConditions(decimal entry, DateTime entryExpiry, decimal target, DateTime targetExpiry)
        {
            PriceEntry = entry;
            EntryExpiry = entryExpiry;
            PriceTarget = target;
            ExpireDate = targetExpiry;
            OnModify?.Invoke(this);
        }

        public void SetTargetPrice(decimal target)
        {
            PriceTarget = target;
        }
    }
}
