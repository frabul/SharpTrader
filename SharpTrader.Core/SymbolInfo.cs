using System.Reflection;

namespace SharpTrader
{ 
    public class SymbolInfo : ISymbolInfo
    {
        public string Key { get; set; }
        public string Asset { get; set; }
        public string QuoteAsset { get; set; }
        public bool IsMarginTadingAllowed { get; internal set; }
        public bool IsCrossMarginAllowed { get; internal set; }
        public bool IsIsolatedMarginAllowed { get; internal set; }
        public bool IsSpotTadingAllowed { get; internal set; }
        public decimal MinLotSize { get; internal set; }
        public decimal LotSizeStep { get; internal set; }
        public decimal MinNotional { get; internal set; }
        public decimal PricePrecision { get; internal set; } 
        public bool IsTradingEnabled { get; internal set; }

        public SymbolInfo()
        {

        }

        public override string ToString()
        {
            return Key;
        }

        public static implicit operator string(SymbolInfo obj)
        {
            return obj.Key;
        }
    }
}
