﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface ITimeRecord
    {
        DateTime Time { get; }
    }

    public class TimeSerie<T> where T : ITimeRecord
    {
        private Stack<int> PositionSaveStack = new Stack<int>();
        private int _Cursor = 0;

        internal List<T> Records { get; set; }

        public int Count { get { return Records.Count; } }

        public DateTime NextTickTime
        {
            get
            {
                if (_Cursor < Records.Count - 1)
                    return Records[_Cursor + 1].Time;
                else
                    return DateTime.MaxValue;
            }
        }

        public DateTime PreviousTickTime
        {
            get
            {
                if (_Cursor > 0)
                    return Records[_Cursor - 1].Time;
                else
                    return DateTime.MinValue;
            }
        }

        public DateTime Time { get { return Records[_Cursor].Time; } }

        public int Position { get { return _Cursor; } }

        public T Tick { get { return Records[_Cursor]; } }
        public T NextTick { get { return Records[_Cursor + 1]; } }
        public T PreviousTick { get { return Records[_Cursor - 1]; } }
        public DateTime LastTickTime { get { return Records[Records.Count - 1].Time; } }
        public DateTime FirstTickTime { get { return Records[0].Time; } }


        /// <summary>
        /// the smallest difference in seconds
        /// </summary>
        /// <param name="smallestFrame"></param>
        public TimeSerie(int capacity)
        {
            Records = new List<T>(capacity);
        }

        struct DummyRecord : ITimeRecord
        {
            public DummyRecord(DateTime time)
            {
                _Time = time;
            }
            DateTime _Time;
            public DateTime Time => _Time;
        }

        public bool TryGetRecord(DateTime time, out T record)
        {

            int ind = BinarySearchByTime(time);
            if (ind > -1)
            {
                record = Records[ind];
                return true;
            }
            else
            {
                record = default(T);
                return false;
            }

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
           
            if (Records.Count > 0 && LastTickTime > time)
                throw new Exception("you cannot add a tick that's preceding the last one ");


            int index = BinarySearchByTime(time);
            if (index > -1)
                Records[index] = historyRecord;
            else
            {
                Records.Add(historyRecord); 
            }
        }

        public void SeekLast()
        {
            _Cursor = Records.Count - 1;
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
                _Cursor = Records.Count - 1;
                return;
            }
            if (date == Records[_Cursor].Time)
                return;
            int lowerLimit = 0;
            int higherLimit = Records.Count - 1;
            int midpoint = 0;
            while (lowerLimit <= higherLimit)
            {
                midpoint = lowerLimit + (higherLimit - lowerLimit) / 2;
                // vediamo se ctime stà tra il punto e quello dopo (visto che dobbiamo prendere il punt appena precedente al ctime)
                if (date >= Records[midpoint].Time && date < Records[midpoint + 1].Time)
                {
                    _Cursor = midpoint;
                    return;
                }
                else if (date < Records[midpoint].Time)
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
            if (_Cursor == Records.Count - 1)
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

        private int BinarySearchByTime(DateTime time)
        {
            var list = Records;

            var lower = 0;
            var upper = list.Count - 1;

            while (lower <= upper)
            {
                var middle = lower + ((upper - lower) / 2);
                var compareResult = list[middle].Time.CompareTo(time);

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
