using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{

    public class TimeSerie<T>
    {
        private Stack<int> PositionSaveStack = new Stack<int>();
        private int _Cursor = 0;

        internal List<T> Records { get; set; }
        internal List<DateTime> Times { get; set; }


        public int Count { get { return Times.Count; } }

        public DateTime NextTickTime
        {
            get
            {
                if (_Cursor < Times.Count - 1)
                    return Times[_Cursor + 1];
                else
                    return DateTime.MaxValue;
            }
        }

        public DateTime PreviousTickTime
        {
            get
            {
                if (_Cursor > 0)
                    return Times[_Cursor - 1];
                else
                    return DateTime.MinValue;
            }
        }

        public DateTime Time { get { return Times[_Cursor]; } }

        public int Position { get { return _Cursor; } }

        public T Tick { get { return Records[_Cursor]; } }
        public T NextTick { get { return Records[_Cursor + 1]; } }
        public T PreviousTick { get { return Records[_Cursor - 1]; } }
        public DateTime LastTickTime { get { return Times[Times.Count - 1]; } }
        public DateTime FirstTickTime { get { return Times[0]; } }

         
        /// <summary>
        /// the smallest difference in seconds
        /// </summary>
        /// <param name="smallestFrame"></param>
        public TimeSerie(int capacity)
        {
            Records = new List<T>(capacity);
            Times = new List<DateTime>(capacity);
        }

        public bool TryGetRecord(int time, ref T record)
        {
            int ind = Times.BinarySearch(time.ToDatetime());
            if (ind > -1)
            {
                record = Records[ind];
                return true;
            }
            else
                return false;
        }

        public T GetLast(Func<T, bool> criteria)
        {
            this.PositionPush();
            this.SeekNearestPreceding(LastTickTime);
            var res = default(T);
            while (this.Previous())
                if (criteria(this.Tick))
                {
                    res = this.Tick;
                    break;
                }
            this.PositionPop();
            return res;
        }

        public void AddRecord(DateTime time, T historyRecord)
        {
            AddRecord((int)time.ToEpoch(), historyRecord);
        }

        public void AddRecord(int cTime, T historyRecord)
        {
            var time = cTime.ToDatetime();
            if (Times.Count > 0 && LastTickTime > time)
                throw new Exception("you cannot add a tick that's preceding the last one ");


            int index = Times.BinarySearch(time);
            if (index > -1)
                Records[index] = historyRecord;
            else
            {
                Records.Add(historyRecord);
                Times.Add(time);
            }
        }

        public void SeekLast()
        {
            _Cursor = Times.Count - 1;
        }

        /// <summary>
        /// Sets the cursor to the nearest tick before  or exacty at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestPreceding(DateTime date)
        {
            if (Records.Count < 1)
                throw new Exception();
            if (date < FirstTickTime)
            {
                throw new Exception("Out of range");
            }
            if (date >= LastTickTime)
            {
                _Cursor = Times.Count - 1;
                return;
            }
            if (date == Times[_Cursor])
                return;
            int lowerLimit = 0;
            int higherLimit = Times.Count - 1;
            int midpoint = 0;
            while (lowerLimit <= higherLimit)
            {
                midpoint = lowerLimit + (higherLimit - lowerLimit) / 2;
                // vediamo se ctime stà tra il punto e quello dopo (visto che dobbiamo prendere il punt appena precedente al ctime)
                if (date >= Times[midpoint] && date < Times[midpoint + 1])
                {
                    _Cursor = midpoint;
                    return;
                }
                else if (date < Times[midpoint])
                    higherLimit = midpoint - 1;
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
            if (_Cursor == Times.Count - 1)
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
    }



}
