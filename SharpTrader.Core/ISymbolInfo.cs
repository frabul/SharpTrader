namespace SharpTrader
{
    public interface ISymbolInfo
    {
        string Asset { get; }
        bool IsMarginTadingAllowed { get; }
        bool IsCrossMarginAllowed { get; }
        bool IsIsolatedMarginAllowed { get; }
        bool IsSpotTadingAllowed { get; }
        bool IsTradingEnabled { get; }
        string Key { get; }
        decimal LotSizeStep { get; }
        decimal MinLotSize { get; }
        decimal MinNotional { get; }
        decimal PricePrecision { get; }
        string QuoteAsset { get; }
    }
}