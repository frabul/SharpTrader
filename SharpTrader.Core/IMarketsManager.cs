using System.Collections.Generic;

namespace SharpTrader
{
    public interface IMarketsManager
    {
        IEnumerable<IMarketApi> Markets { get; }

        IMarketApi GetMarketApi(string marketName);
        IEnumerable<string> GetSymbols(string market);
    }
}