using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{

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
        public static CandlestickTimeComparer Instance { get; } = new CandlestickTimeComparer();
        public int Compare(ITradeBar x, ITradeBar y)
        {
           
            return x.OpenTime.CompareTo(y.OpenTime);
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

    public static class Utils
    {
        public static decimal RoundNumberHigher(decimal x, decimal precision)
        {
            if (precision != 0)
            {
                var resto = x % precision;
                x = x - resto + precision;
            }
            return x;
        }
        public static decimal RoundNumberLower(decimal x, decimal precision)
        {
            if (precision != 0)
            {
                var resto = x % precision;
                x = x - resto;
            }
            return x;
        }
    }

}
