using System;
using System.Collections.Generic;

namespace SharpTrader.AlgoFramework
{
    public class TimeSlice
    {
        private Dictionary<string, SymbolData> _SymbolsData = new Dictionary<string, SymbolData>();
        private List<Signal> _NewSignals = new List<Signal>(20);
        private List<Operation> _NewOperations = new List<Operation>(20);
        private List<ITrade> _Trades = new List<ITrade>();

        public IReadOnlyDictionary<string, SymbolData> SymbolsData => _SymbolsData;
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public IReadOnlyList<Signal> NewSignals => _NewSignals;

        public IReadOnlyList<Operation> NewOperations => _NewOperations;

        public IReadOnlyList<ITrade> Trades => _Trades;

        public void Add(Signal newSignal)
        {
            var data = GetOrAddData(newSignal.Symbol);
            data.Add(newSignal);
            _NewSignals.Add(newSignal);
        }

        public void Add(Operation newOper)
        {
            var data = GetOrAddData(newOper.Symbol);
            data.Add(newOper);
            _NewOperations.Add(newOper);
        }

        public void Add(SymbolInfo symbol, IBaseData dataRecord)
        {
            var data = GetOrAddData(symbol);
            data.Add(dataRecord);
        }

        public void Add(SymbolInfo symbol, ITrade trade)
        {
            var data = GetOrAddData(symbol);
            data.Add(trade);
            _Trades.Add(trade);
        }

        public void Clear(DateTime time)
        {
            StartTime = time;
            _NewSignals.Clear();
            _NewOperations.Clear();
            _Trades.Clear();
            _SymbolsData.Clear();
        }

        private SymbolData GetOrAddData(SymbolInfo symbol)
        {
            if (!SymbolsData.TryGetValue(symbol.Key, out SymbolData data))
            {
                data = new SymbolData(symbol);
                _SymbolsData[symbol.Key] = data;
            }
            return data;
        }


        //--------------------------- End of TimeSlice class --------------------------
        public class SymbolData
        {
            //todo maybe use lazy initialization for these collections
            private List<Signal> _NewSignals = new List<Signal>(2);
            private List<Operation> _NewOperations = new List<Operation>(2);
            private List<ITrade> _Trades = new List<ITrade>(2);
            private List<IBaseData> _Records = new List<IBaseData>(2);
            public SymbolInfo Symbol { get; }
            public IReadOnlyList<IBaseData> Records => _Records;
            public IReadOnlyList<Signal> NewSignals => _NewSignals;
            public IReadOnlyList<Operation> NewOperations => _NewOperations;
            public IReadOnlyList<ITrade> Trades => _Trades;

            public SymbolData(SymbolInfo info)
            {
                this.Symbol = info;
            }

            internal void Add(IBaseData dataRecord)
            {
                _Records.Add(dataRecord);
            }

            internal void Add(ITrade trade)
            {
                _Trades.Add(trade);
            }

            internal void Add(Operation oper)
            {
                _NewOperations.Add(oper);
            }
            internal void Add(Signal oper)
            {
                _NewSignals.Add(oper);
            }
        }
    }

}
