using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class CandlesticksSerieNavigator
    {
        private Stack<int> PositionSaveStack = new Stack<int>();
        private static CandlestickTimeComparer CandlestickTimeComparer = new CandlestickTimeComparer();
        private ITradeBar[] Records;

        private int _Cursor = 0;
        public int Count { get { return Records.Length; } }


        public DateTime NextTickTime
        {
            get
            {
                if (_Cursor < Records.Length - 1)
                    return Records[_Cursor + 1].OpenTime;
                else
                    return DateTime.MaxValue;
            }
        }

        public DateTime PreviousTickTime
        {
            get
            {
                if (_Cursor > 0)
                    return Records[_Cursor - 1].OpenTime;
                else
                    return DateTime.MinValue;
            }
        }

        public DateTime Time { get { return Records[_Cursor].OpenTime; } }

        public int Position { get { return _Cursor; } }
        public ITradeBar Tick { get { return Records[_Cursor]; } }
        public ITradeBar NextTick { get { return Records[_Cursor + 1]; } }
        public ITradeBar PreviousTick { get { return Records[_Cursor - 1]; } }
        public DateTime LastTickTime { get { return Records[Records.Length - 1].OpenTime; } }
        public DateTime FirstTickTime { get { return Records[0].OpenTime; } }

      
        public CandlesticksSerieNavigator(IList<Candlestick> list)
        {
            Records = list.Cast<ITradeBar>().ToArray();
        }

        public bool TryGetRecord(DateTime time, out ITradeBar record)
        {
            record = null;

            int ind = BinarySearchByOpenTime(time);
            if (ind > -1)
            {
                record = Records[ind];
                return true;
            }
            else
                return false;
        }

        public ITradeBar GetLast(Func<ITradeBar, bool> criteria)
        {
            this.PositionPush();
            this.SeekNearestPreceding(LastTickTime);
            ITradeBar res = null;
            while (this.Previous())
                if (criteria(this.Tick))
                {
                    res = this.Tick;
                    break;
                }
            this.PositionPop();
            return res;
        }


        public void SeekLast()
        {
            _Cursor = Records.Length - 1;
        }

        /// <summary>
        /// Sets the cursor to the nearest tick before  or exacty at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestPreceding(DateTime date)
        {
            if (Records.Length < 1)
                throw new Exception();
            if (date < FirstTickTime)
            {
                throw new Exception("Out of range");
            }
            if (date >= LastTickTime)
            {
                _Cursor = Records.Length - 1;
                return;
            }
            if (date == Records[_Cursor].OpenTime)
                return;
            int lowerLimit = 0;
            int upperLimit = Records.Length - 1;
            int midpoint = 0;
            while (lowerLimit <= upperLimit)
            {
                midpoint = lowerLimit + (upperLimit - lowerLimit) / 2;
                // vediamo se ctime stà tra il punto e quello dopo (visto che dobbiamo prendere il punt appena precedente al ctime)
                if (date >= Records[midpoint].OpenTime && date < Records[midpoint + 1].OpenTime)
                {
                    _Cursor = midpoint;
                    return;
                }
                else if (date < Records[midpoint].OpenTime)
                    upperLimit = midpoint - 1;
                else
                    lowerLimit = midpoint + 1;
            }

        }

        public void SeekNearestPreceding(int unixTime)
        {
            SeekNearestPreceding(unixTime.ToDatetime());
        }

        /// <summary>
        /// ritorna true se ci sono altri cosi in avanti cursore di uno in avanti
        /// </summary>
        public bool Next()
        {
            if (_Cursor == Records.Length - 1)
                return false;
            _Cursor++;
            return true;
        }

        /// <summary>
        /// returns false if the serie is finished
        /// </summary>
        /// <returns></returns>
        public bool Previous()
        {
            if (_Cursor == 0)
                return false;
            _Cursor--;
            return true;
        }

        public void PositionPush()
        {
            PositionSaveStack.Push(_Cursor);
        }

        public void PositionPop()
        {
            _Cursor = PositionSaveStack.Pop();
        }


        private int BinarySearchByOpenTime(DateTime openTime)
        {
            var list = Records;

            var lower = 0;
            var upper = list.Length - 1;

            while (lower <= upper)
            {
                var middle = lower + ((upper - lower) / 2);
                var compareResult = list[middle].OpenTime.CompareTo(openTime);

                if (compareResult == 0)
                    return middle;
                if (compareResult > 0)
                    upper = middle - 1;
                else
                    lower = middle + 1;
            }

            return ~lower;
        }


    }
}
