using BinanceExchange.API.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpTrader.Core.BrokersApi.Binance
{
    public class BinanceSymbolInfo : ISymbolInfo
    {
        public string Key { get; private set; }
        public string Asset { get; private set; }
        public string QuoteAsset { get; private set; }
        public bool IsMarginTadingAllowed { get; private set; }
        public bool IsCrossMarginAllowed { get; set; }
        public bool IsIsolatedMarginAllowed => IsMarginTadingAllowed;
        public bool IsSpotTadingAllowed { get; private set; }
        public decimal MinLotSize { get; private set; }
        public decimal LotSizeStep { get; private set; }
        public decimal MinNotional { get; private set; }
        public decimal PricePrecision { get; private set; }
        public bool IsTradingEnabled { get; set; }
        public BinanceSymbolInfo() { }
        public BinanceSymbolInfo(string key)
        {
            Key = key;
        }

        public BinanceSymbolInfo(ExchangeInfoSymbol binanceSymbol)
        {
            Update(binanceSymbol);
        }

        public void Update(ExchangeInfoSymbol binanceSymbol)
        {
            var lotSize = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterLotSize) as ExchangeInfoSymbolFilterLotSize;
            var minNotional = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterMinNotional) as ExchangeInfoSymbolFilterMinNotional;
            var pricePrecision = binanceSymbol.filters.First(f => f is ExchangeInfoSymbolFilterPrice) as ExchangeInfoSymbolFilterPrice;

            Key = binanceSymbol.symbol;
            Asset = binanceSymbol.baseAsset;
            QuoteAsset = binanceSymbol.quoteAsset;
            IsTradingEnabled = binanceSymbol.status == "TRADING";
            IsMarginTadingAllowed = binanceSymbol.isMarginTradingAllowed;
            IsSpotTadingAllowed = binanceSymbol.isSpotTradingAllowed;

            LotSizeStep = lotSize.StepSize;
            MinLotSize = lotSize.MinQty;
            MinNotional = minNotional.MinNotional;
            PricePrecision = pricePrecision.TickSize;
        }

        public override string ToString()
        {
            return Key;
        }

        public static implicit operator string(BinanceSymbolInfo obj)
        {
            return obj.Key;
        }
    }
}
