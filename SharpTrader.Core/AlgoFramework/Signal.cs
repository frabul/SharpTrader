using System;
using System.Dynamic;

namespace SharpTrader.AlgoFramework
{
    public class Signal
    {
        public Signal(SymbolInfo symbol, SignalKind kind, DateTime creationTime)
        {
            Symbol = symbol;
            Kind = kind;
            CreationTime = creationTime;
        }

        public event Action<Signal> OnModify;
        /// <summary>
        /// The operation managing this signal ( if any )
        /// </summary>
        public Operation Operation { get; set; }
        public SymbolInfo Symbol { get; set; }

        public SignalKind Kind { get; set; }
        public DateTime CreationTime { get; set; } 
        public dynamic AdditionalInfo { get; } = new ExpandoObject();
        public decimal PriceTarget { get; set; }

        /// <summary>
        /// If it's a buy operation this is the maximum price to buy
        /// If it's a sell operation this indicates the minimum price to sell
        /// </summary>
        public decimal PriceEntry { get; set; }

        /// <summary>
        /// Any operation 
        /// </summary>
        public DateTime EntryExpiry { get; set; }

        /// <summary>
        /// Every operation based on this signal is meant to be closed after this time
        /// </summary>
        public DateTime ExpireDate { get; set; }

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
