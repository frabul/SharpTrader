using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract class SymbolsSelector
    {
        private Dictionary<string, ISymbolInfo> _SymbolsSelected = new Dictionary<string, ISymbolInfo>();

        public IReadOnlyDictionary<string, ISymbolInfo> SymbolsSelected => _SymbolsSelected;
        public TradingAlgo Algo { get; private set; }
        public DateTime NextSwapTime { get; private set; }
        public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromHours(24);
        /// <summary>
        /// Returns all symbols that could potentially be selected for trading
        /// </summary>
        public abstract ISymbolInfo[] SymbolsPool { get; }
        protected abstract Task<ISymbolInfo[]> OnUpdate(TimeSlice slice);

        protected abstract Task OnInitialize();

        public Task Initialize(TradingAlgo algo)
        {
            Algo = algo;
            return OnInitialize();
        }

        public async Task<SelectedSymbolsChanges> UpdateAsync(TimeSlice slice)
        {
            if (NextSwapTime < Algo.Market.Time)
            {
                NextSwapTime = Algo.Market.Time + UpdatePeriod;

                var filtered = await OnUpdate(slice);

                var removedSymbols =
                    (from sym in _SymbolsSelected.Values where !filtered.Any(s => s.Key == sym.Key) select sym)
                    .ToArray();
                var added = (from sym in filtered where !_SymbolsSelected.ContainsKey(sym.Key) select sym).ToArray();

                //remove unused symbols
                foreach (var sym in removedSymbols)
                {
                    _SymbolsSelected.Remove(sym.Key);
                }

                //add new symbols
                foreach (var sym in added)
                {
                    ISymbolFeed feed = await Algo.GetSymbolFeed(sym.Key);
                    _SymbolsSelected.Add(feed.Symbol.Key, feed.Symbol);
                }
                var retVal = new SelectedSymbolsChanges(added.ToArray(), removedSymbols.ToArray());
                return retVal;
            }

            return SelectedSymbolsChanges.None;
        }
    }

    public class SelectedSymbolsChanges
    {
        public ISymbolInfo[] AddedSymbols { get; private set; }
        public ISymbolInfo[] RemovedSymbols { get; private set; }
        public static SelectedSymbolsChanges None { get; internal set; } = new SelectedSymbolsChanges(new ISymbolInfo[0], new ISymbolInfo[0]);
        public int Count => AddedSymbols.Length + RemovedSymbols.Length;

        public SelectedSymbolsChanges(ISymbolInfo[] addedSymbols, ISymbolInfo[] removedSymbols)
        {
            AddedSymbols = addedSymbols;
            RemovedSymbols = removedSymbols;
        }
    }
}
