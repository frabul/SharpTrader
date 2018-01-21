using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface ITimeRecord
    {
        DateTime Time { get; }
    }

    public class TimeSerieNavigator<T> where T : ITimeRecord
    {
        private int _Cursor = -1;
        private Stack<int> PositionSaveStack = new Stack<int>();
        protected TimeRecordCollection<T> Records { get; private set; }

        public int Count { get { return Records.Count; } }

        public DateTime NextTickTime => _Cursor < Records.Count - 1 ?
                                            Records[_Cursor + 1].Time : DateTime.MaxValue;

        public DateTime PreviousTickTime => _Cursor > 0 ?
                                            Records[_Cursor - 1].Time : DateTime.MinValue;

        public DateTime Time => _Cursor > -1 ? Records[_Cursor].Time : DateTime.MinValue;

        public int Position => _Cursor;

        public T Tick => Records[_Cursor];
        public T NextTick => Records[_Cursor + 1];
        public T PreviousTick => Records[_Cursor - 1];
        public DateTime LastTickTime => Records[Records.Count - 1].Time;
        public DateTime FirstTickTime => Records[0].Time;

        public T LastTick => Records[Records.Count - 1];

        public bool EndOfSerie => _Cursor >= Records.Count - 1;


        public TimeSerieNavigator()
        {
            Records = new TimeRecordCollection<T>();
        }

        public TimeSerieNavigator(TimeSerieNavigator<T> items)
        {
            Records = items.Records;
        }
        public TimeSerieNavigator(IEnumerable<T> items)
        {
            Records = new TimeRecordCollection<T>(items);
        }
        public T GetFromCursor(int count)
        {
            return Records[_Cursor - count];
        }
        public bool TryGetRecord(DateTime time, out T record)
        {

            int ind = Records.BinarySearch(time);
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

        public void SeekFirst()
        {
            if (Count < 1)
                throw new InvalidOperationException("The serie has no elements.");
            _Cursor = 0;
        }

        public void SeekLast()
        {
            _Cursor = Records.Count - 1;
        }

        /// <summary>
        /// Sets the cursor to the nearest tick before  or exacty at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestBefore(DateTime date)
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
            Debug.Assert(_Cursor > -1);
        }

        public void SeekNearestBefore(int unixTime)
        {
            SeekNearestBefore(unixTime.ToDatetime());
        }

        public void SeekNearestAfter(DateTime time)
        {
            var ind = Records.BinarySearch(time);
            if (ind > -1)
                _Cursor = ind;
            else
                _Cursor = ~ind;
            Debug.Assert(_Cursor > -1);
        }

        /// <summary>
        /// ritorna true se ci sono altri cosi in avanti cursore di uno in avanti
        /// </summary>
        public bool Next()
        {
            if (_Cursor == Records.Count - 1)
                return false;
            _Cursor++;
            Debug.Assert(_Cursor > -1);
            return true;
        }

        /// <summary>
        /// returns false if the serie is finished
        /// </summary>
        /// <returns></returns>
        public bool Previous()
        {
            if (_Cursor < 1)
                return false;
            _Cursor--;
            Debug.Assert(_Cursor > -1);
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


    public class TimeRecordCollection<T> where T : ITimeRecord
    {
        private List<T> Items;
        ReaderWriterLock Lock = new ReaderWriterLock();

        public int Count => Items.Count;


        public TimeRecordCollection()
        {
            Items = new List<T>();
        }

        public TimeRecordCollection(int capacity)
        {
            Items = new List<T>(capacity);
        }

        public TimeRecordCollection(IEnumerable<T> items)
        {
            Items = items.ToList();
        }

        public T this[int index]
        {
            get
            {
                Lock.AcquireReaderLock(int.MaxValue);
                try
                {
                    return Items[index];
                }
                catch
                {
                    throw;
                }
                finally
                {
                    Lock.ReleaseLock();
                }
            }
            set
            {
                try
                {
                    Items[index] = value;
                }
                catch
                {
                    throw;
                }
                finally
                {
                    Lock.ReleaseLock();
                }
            }
        }

        public retType ReadOperation<retType>(Func<retType> func)
        {
            Lock.AcquireReaderLock(int.MaxValue);
            try
            {
                return func();
            }
            catch
            {
                throw;
            }
            finally
            {
                Lock.ReleaseLock();
            }
        }

        public void WriteOperation(Action func)
        {
            Lock.AcquireWriterLock(int.MaxValue);
            try
            {
                func();
            }
            catch
            {
                throw;
            }
            finally
            {
                Lock.ReleaseLock();
            }
        }

        public void Add(T item)
        {
            WriteOperation(() => Items.Add(item));
        }
        public void AddRange(IEnumerable<T> items)
        {
            WriteOperation(() => Items.AddRange(items));
        }
        public void RemoveAt(int ind)
        {
            throw new NotImplementedException();
        }
        public int BinarySearch(DateTime time)
        {
            return ReadOperation<int>(() => BinarySearch_Internal(time));
        }

        private int BinarySearch_Internal(DateTime time)
        {
            var list = Items;
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
