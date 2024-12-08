using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class StaticSymbolsSelector : SymbolsSelector
    {
        public string[] SymbolsKeys { get; }
        public ISymbolInfo[] Symbols { get; private set; }

        public override ISymbolInfo[] SymbolsPool => Symbols;

        public StaticSymbolsSelector(IEnumerable<string> symbolsKeys)
        {
            SymbolsKeys = symbolsKeys.ToArray();
        }

        protected override ISymbolInfo[] OnUpdate(TimeSlice slice)
        {
            return Symbols;
        }

        protected override Task OnInitialize()
        {
            Symbols = Algo.Market.GetSymbols().Where(s => SymbolsKeys.Any(sk => sk == s.Key)).ToArray();
            return Task.CompletedTask;
        }
    } 
}
