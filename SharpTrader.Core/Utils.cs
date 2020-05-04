using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class AssetAmount
    {
        public string Asset { get; set; }
        public decimal Amount { get; set; }

        public AssetAmount(string asset, decimal budget)
        {
            Asset = asset;
            Amount = budget;
        }

        public static decimal Convert(AssetAmount amount, string targetAsset, IEnumerable<ISymbolFeed> feeds  )
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
        public static decimal Convert(AssetAmount amount, string targetAsset, ISymbolFeed feed)
        {
            if (amount.Asset == targetAsset)
                return amount.Amount; 
            if (feed == null)
                throw new ArgumentException("No feed provieded for the conversion");
            if (feed.Symbol.Asset == targetAsset && feed.Symbol.QuoteAsset == amount.Asset)
                return amount.Amount / (decimal)feed.Ask;
            else if (feed.Symbol.QuoteAsset == targetAsset && feed.Symbol.Asset == amount.Asset)
                return amount.Amount * (decimal)feed.Bid;
            else
                throw new ArgumentException("The symbol feed is doesn't correspond to the assets pair");
        }
    }

    public static class Extensions
    {
        public static readonly DateTime BaseUnixTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long ToEpoch(this DateTime date)
        {
            return (long)Math.Round((date - Extensions.BaseUnixTime).TotalSeconds);
        }
        public static long ToEpochMilliseconds(this DateTime date)
        {
            return (long)Math.Round((date - Extensions.BaseUnixTime).TotalMilliseconds);
        }
        public static DateTime ToDatetime(this long epoch)
        {
            return Extensions.BaseUnixTime.AddSeconds(epoch);
        }
        public static DateTime ToDatetimeMilliseconds(this long epoch)
        {
            return Extensions.BaseUnixTime.AddMilliseconds(epoch);
        }
        public static DateTime ToDatetime(this int epoch)
        {
            return Extensions.BaseUnixTime.AddSeconds(epoch);
        }
     
        public static bool EpsilonEqual(this double x1, double x2, double epsilon) => Math.Abs((x1 - x2) / x1) < epsilon;
        public static bool EpsilonEqual(this decimal x1, decimal x2, double epsilon) => Math.Abs((x1 - x2) / x1) < (decimal)epsilon;
    }
    public class CandlestickTimeComparer<Tc> : IComparer<Tc> where Tc : ITradeBar
    {
        public int Compare(Tc x, Tc y)
        {
            return x.Time.CompareTo(y.Time);
        }
    }
 
 


    public class CandlestickTimeComparer : IComparer<ITradeBar>
    {
        public int Compare(ITradeBar x, ITradeBar y)
        {
            return x.OpenTime.CompareTo(y);
            //return (int)(x.OpenTime.Ticks - y.OpenTime.Ticks);
            //var val = x.OpenTime.Ticks - y.OpenTime.Ticks;
            //if (val > int.MaxValue)
            //    return int.MaxValue;
            //else if (val < int.MinValue)
            //    return int.MinValue;
            //else
            //    return (int)val;
        }
    }

}
