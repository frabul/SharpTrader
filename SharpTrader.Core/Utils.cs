using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public static class Extensions
    {
        public static readonly DateTime BaseUnixTime = new DateTime(1970, 1, 1);
        public static long ToEpoch(this DateTime date)
        {
            return (long)Math.Round((date - Extensions.BaseUnixTime).TotalSeconds);
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


    }
    public class CandlestickTimeComparer<Tc> : IComparer<Tc> where Tc : ICandlestick
    {
        public int Compare(Tc x, Tc y)
        {
            //return (int)(x.OpenTime.Ticks - y.OpenTime.Ticks);
            var val = x.OpenTime.Ticks - y.OpenTime.Ticks;
            if (val > int.MaxValue)
                return int.MaxValue;
            else if (val < int.MinValue)
                return int.MinValue;
            else
                return (int)val;
        }
    }
    public class CandlestickTimeComparer : IComparer<ICandlestick>
    {
        public int Compare(ICandlestick x, ICandlestick y)
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
