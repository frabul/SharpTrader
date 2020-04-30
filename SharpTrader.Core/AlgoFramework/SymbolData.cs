using LiteDB;
using System.Collections.Generic;

namespace SharpTrader.AlgoFramework
{
    public class SymbolData
    {
        [BsonId] public string Id => Symbol.Key;
        public SymbolInfo Symbol { get; set; }

        /// <summary>
        /// Feed is automaticly initialized by the algo for the symbols requested by the symbols selector module
        /// </summary>
        [BsonIgnore] public ISymbolFeed Feed { get; set; }

        [BsonIgnore] public List<Operation> ActiveOperations { get; set; } = new List<Operation>();
        [BsonIgnore] public List<Operation> ClosedOperations { get; set; } = new List<Operation>();

        /// <summary>
        /// This property can be used by sentry module to store its data
        /// </summary>
        public object SentryData { get; set; }
        public object OperationsManagerData { get; set; }
        public object AllocatorData { get; set; }
        public object RiskManagerData { get; set; }

        
        public SymbolData(SymbolInfo symbol)
        {
            Symbol = symbol;
        }
        public SymbolData()
        {

        }

    }

}
