using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Dynamic;
using System.Reflection;

namespace SharpTrader.AlgoFramework
{
    public class Signal : IChangeTracking
    {
        public event Action<Signal> OnModify;
        private volatile bool _IsChanged = true;
        /// <summary>
        /// Constructor used by serialization library
        /// </summary>
        public Signal()
        {

        }

        public Signal(string _id, ISymbolInfo symbol, SignalKind kind, DateTime creationTime)
        {
            Id = _id;
            Symbol = symbol;
            Kind = kind;
            ModifyTime = creationTime;
        }

        /// <summary>
        /// Constructor dedicated to serialization
        /// </summary> 
        internal Signal(string id)
        {
            Id = id;
        }
        [BsonId]
        public string Id { get; }

        /// <summary>
        /// The operation managing this signal ( if any )
        /// </summary>
        [BsonIgnore]
        public Operation Operation { get; set; }

        public ISymbolInfo Symbol { get; private set; }
        public SignalKind Kind { get; private set; }

        [BsonField("CreationTime")]
        public DateTime ModifyTime { get; private set; }
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

        public bool IsChanged => _IsChanged;

        public void ModifyConditions(DateTime timeNow, decimal entry, DateTime entryExpiry, decimal target, DateTime targetExpiry)
        {
            ModifyTime = timeNow;
            PriceEntry = entry;
            EntryExpiry = entryExpiry;
            PriceTarget = target;
            ExpireDate = targetExpiry;
            _IsChanged = true;
            OnModify?.Invoke(this);
        }

        public void SetTargetPrice(decimal target)
        {
            PriceTarget = target;
            _IsChanged = true;
        }

        public void AcceptChanges()
        {
            _IsChanged = false;
        }

        public override bool Equals(object obj)
        {
            if (obj is Signal sign)
                return sign.Id == Id;
            return false;
        }
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
