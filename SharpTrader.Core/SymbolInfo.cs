namespace SharpTrader
{
    public class SymbolInfo : ISymbolInfo
    {
        public string Key { get; set; }
        public string Asset { get; set; }
        public string QuoteAsset { get; set; }
        public bool IsMarginTadingAllowed { get; set; }
        public bool IsSpotTadingAllowed { get; set; }
        public decimal MinLotSize { get; set; }
        public decimal LotSizeStep { get; set; }
        public decimal MinNotional { get; set; }
        public decimal PricePrecision { get; set; }
        public bool IsBorrowAllowed { get; set; }

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
