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
    }
}
