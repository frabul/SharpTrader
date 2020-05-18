using LiteDB;
using System.Collections.Generic;

namespace SharpTrader.AlgoFramework
{
    public class SymbolData
    {
        private List<Operation> _ActiveOperations { get; } = new List<Operation>();
        private List<Operation> _ClosedOperations { get; } = new List<Operation>(); 
        public SymbolInfo Symbol { get; set; }
        [BsonId] public string Id => Symbol.Key;
      
        /// <summary>
        /// Feed is automaticly initialized by the algo for the symbols requested by the symbols selector module
        /// </summary>
        [BsonIgnore] public ISymbolFeed Feed { get; set; } 
        [BsonIgnore] public IReadOnlyList<Operation> ActiveOperations => _ActiveOperations;
        [BsonIgnore] public IReadOnlyList<Operation> ClosedOperations => _ClosedOperations;

        /// <summary>
        /// This property can be used by sentry module to store its data
        /// </summary>
        public object SentryData { get; set; }
        public object OperationsManagerData { get; set; }
        public object AllocatorData { get; set; }
        public object RiskManagerData { get; set; }

        [BsonIgnore] public bool IsSelectedForTrading { get; internal set; } = false;

        public SymbolData(SymbolInfo symbol)
        {
            Symbol = symbol;
        }
        public SymbolData()
        {

        }

        public void AddActiveOperation(Operation op)
        {
            _ActiveOperations.Add(op); 
        }
        public void RemoveActiveOperation(Operation op)
        {
            _ActiveOperations.Remove(op);
            //if there wasn't any transaction for the operation then we just forget it
            if (op.AmountInvested > 0)
                this._ClosedOperations.Add(op);
        }

     
    }

}
