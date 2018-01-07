using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core
{
    public class CandlesticksTimeSerie<T> where T : ICandlestick
    {
        private static CandlestickTimeComparer<T> CandlestickTimeComparer = new CandlestickTimeComparer<T>();
        List<T> Ticks;
        public CandlesticksTimeSerie(List<T> list)
        {
            Ticks = list;
        }

        public void AddCandle(T candle)
        {
            int index = Ticks.BinarySearch(candle, CandlestickTimeComparer);

            if (index > -1)
                Ticks[index] = candle;
            else
            {
                Ticks.Insert(~index, candle);
            }
        }


    }
}
