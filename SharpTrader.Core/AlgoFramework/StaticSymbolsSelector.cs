using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class StaticSymbolsSelector : SymbolsSelector
    {
        public ISymbolInfo[] Symbols { get; private set; }

        public override ISymbolInfo[] SymbolsPool => Symbols;

        public StaticSymbolsSelector(IEnumerable<ISymbolInfo> symbolsKeys)
        {
            Symbols = symbolsKeys.ToArray();
        }

        protected override ISymbolInfo[] OnUpdate(TimeSlice slice)
        {
            return Symbols;
        }

        protected override async Task OnInitialize()
        {
            foreach (var symbol in Symbols)
            { 
                _ = await Algo.Market.GetSymbolFeedAsync(symbol.Key); 
            }
        }
    }
}
