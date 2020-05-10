namespace SharpTrader
{
    public interface ISymbolInfo
    {
        string Asset { get;  }
        bool IsBorrowAllowed { get;  }
        bool IsMarginTadingAllowed { get;  }
        bool IsSpotTadingAllowed { get;  }
        string Key { get;  }
        decimal LotSizeStep { get; }
        decimal MinLotSize { get;   }
        decimal MinNotional { get;   }
        decimal PricePrecision { get;  }
        string QuoteAsset { get;   }
    }
}