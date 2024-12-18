using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace SharpTrader
{
    [Obfuscation(Exclude = true)]
    public class AssetAmount
    {
        public string Asset { get; set; }
        public decimal Amount { get; set; }
        /// <summary>
        /// To use only for serialization
        /// </summary>
        public AssetAmount() { }
        public AssetAmount(string asset, decimal budget)
        {
            Asset = asset;
            Amount = budget;
        }

        public static decimal Convert(AssetAmount amount, string targetAsset, IEnumerable<ISymbolFeed> feeds)
        {
            if (amount.Asset == targetAsset)
                return amount.Amount;
            var feed = feeds.FirstOrDefault();
            if (feed == null)
                throw new ArgumentException("No feed provieded for the conversion");
            if (feed.Symbol.Asset == targetAsset && feed.Symbol.QuoteAsset == amount.Asset)
                return amount.Amount / (decimal)feed.Ask;
            else if (feed.Symbol.QuoteAsset == targetAsset && feed.Symbol.Asset == amount.Asset)
                return amount.Amount * (decimal)feed.Bid;
            else
                throw new ArgumentException("The symbol feed is doesn't correspond to the assets pair");
        }
        /// <summary>
        /// Convert the amount to the target asset
        /// Allows to specify a custom target price for the conversion
        /// </summary>
        public static decimal Convert(AssetAmount amount, string targetAsset, ISymbolFeed feed, decimal? target_price = null)
        {
            if (amount.Asset == targetAsset)
                return amount.Amount;
            if (feed == null)
                throw new ArgumentException("No feed provieded for the conversion");
            if (feed.Symbol.Asset == targetAsset && feed.Symbol.QuoteAsset == amount.Asset)
                return amount.Amount / (target_price == null ? (decimal)feed.Ask : target_price.Value);
            else if (feed.Symbol.QuoteAsset == targetAsset && feed.Symbol.Asset == amount.Asset)
                return amount.Amount * (target_price == null ? (decimal)feed.Bid : target_price.Value);
            else
                throw new ArgumentException("The symbol feed is doesn't correspond to the assets pair");
        }


        public override string ToString()
        {
            return $"{{ {Asset}, {Amount:f9} }}";
        }

    }

}
