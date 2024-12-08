using LiteDB;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SharpTrader.AlgoFramework
{
    [Obfuscation(Exclude = true)]
    public class SymbolData
    {
        private HashSet<Operation> _ActiveOperations { get; } = new HashSet<Operation>();

        public ISymbolInfo Symbol { get; set; }
        [BsonId] public string Id => Symbol.Key;

        /// <summary>
        /// Feed is automaticly initialized by the algo for the symbols requested by the symbols selector module
        /// </summary>
        [BsonIgnore] public ISymbolFeed Feed { get; set; }
        [BsonIgnore] public HashSet<Operation> ActiveOperations => _ActiveOperations;

        /// <summary>
        /// This property can be used by sentry module to store its data
        /// </summary>
        public object SentryData { get; set; }
        public object OperationsManagerData { get; set; }
        public object AllocatorData { get; set; }
        public object RiskManagerData { get; set; }

        [BsonIgnore] public bool IsSelectedForTrading { get; internal set; } = false;

        public SymbolData(ISymbolInfo symbol)
        {
            Symbol = symbol;
        }
        public SymbolData()
        {

        }

        internal void AddActiveOperation(Operation op)
        {
            _ActiveOperations.Add(op);
        }
        internal void CloseOperation(Operation op)
        {
            _ActiveOperations.Remove(op);
        }
    }

}
