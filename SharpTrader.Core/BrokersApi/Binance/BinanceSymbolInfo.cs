using BinanceExchange.API.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpTrader.Core.BrokersApi.Binance
{
    public class BinanceSymbolInfo : ISymbolInfo
    {
        public string Key { get; set; }
        public string Asset { get; set; }
        public string QuoteAsset { get; set; }
        public bool IsMarginTadingAllowed { get; set; }
        public bool IsCrossMarginAllowed { get; set; }
        public bool IsIsolatedMarginAllowed { get; set; }
        public bool IsSpotTadingAllowed { get; set; }
        public decimal MinLotSize { get; set; }
        public decimal LotSizeStep { get; set; }
        public decimal MinNotional { get; set; }
        public decimal PricePrecision { get; set; }
        public bool IsTradingEnabled { get; set; }
        public BinanceSymbolInfo() { }
        public BinanceSymbolInfo(string key)
        {
            Key = key;
        }

        public BinanceSymbolInfo(ExchangeInfoSymbol binanceSymbol)
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
