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

    public class TimeSerieNavigator<T> : IDisposable where T : ITimeRecord
    {
        private object Locker = new object();
        public event Action<T> OnNewRecord;

        private int _Cursor = -1;
        private Stack<int> PositionSaveStack = new Stack<int>();
        private bool disposed;
        private TimeSerieNavigator<ITradeBar> items;

        protected TimeRecordCollection<T> Records { get; private set; }

        public int Count { get { return Records.Count; } }

        public DateTime NextTickTime => Cursor < Records.Count - 1 ?
                                            Records[Cursor + 1].Time : DateTime.MaxValue;

        public DateTime PreviousTickTime => Cursor > 0 ?
                                            Records[Cursor - 1].Time : DateTime.MinValue;

        public DateTime Time => Cursor > -1 ? Records[Cursor].Time : DateTime.MinValue;

        public int Position => Cursor;

        public T Current => Records[Cursor];
        public T Next => Records[Cursor + 1];
        public T Previus => Records[Cursor - 1];
        public DateTime EndTime => Records[Records.Count - 1].Time;
        public DateTime StartTime => Records[0].Time;

        public T Last => Records[Records.Count - 1];

        public bool EndOfSerie => Cursor >= Records.Count - 1;

        public int Cursor { get { lock (Locker) return _Cursor; } set { lock (Locker) _Cursor = value; } }

        public TimeSerieNavigator()
        {
            Records = new TimeRecordCollection<T>();
            Records.NewRecord += Records_NewRecord; ;
            Records.OnShrink += OnRecordsShrunk;
        }

        public TimeSerieNavigator(TimeSerieNavigator<T> items)
        {
            Records = items.Records;
            Records.NewRecord += Records_NewRecord;
            Records.OnShrink += OnRecordsShrunk;
        }

        public TimeSerieNavigator(IEnumerable<T> items)
        {
            Records = new TimeRecordCollection<T>(items);
            Records.NewRecord += Records_NewRecord;
            Records.OnShrink += OnRecordsShrunk;
        }



        private void Records_NewRecord(TimeRecordCollection<T> sender, T rec)
        {
            OnNewRecord?.Invoke(rec);
        }

        private void OnRecordsShrunk(TimeRecordCollection<T> serie, int oldItemsCount)
        {
            lock (Locker)
            {
                int itemsRemoved = oldItemsCount - serie.Count;
                Cursor = Cursor - itemsRemoved;
                Debug.Assert(Cursor > -1 || Cursor <= -itemsRemoved, "Cursor is meant to be >= 0 unless it was 0 ( - itemsRemoved now )");
                if (Cursor < 0)
                    Cursor = 0;
            }
        }

        public T GetFromCursor(int ind)
        {
            lock (Locker)
                return Records[Cursor - ind];
        }

        public T GetFromLast(int ind)
        {
            lock (Locker)
                return Records[Records.Count - 1 - ind];
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
            lock (Locker)
            {
                if (Count < 1)
                    throw new InvalidOperationException("The serie has no elements.");
                Cursor = 0;
            }

        }

        public void SeekLast()
        {
            Cursor = Records.Count - 1;
        }


        /// <summary>
        /// Sets the cursor to the nearest tick before  or exacty at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestBefore(DateTime date)
        {
            lock (Locker)
            {
                if (Records.Count < 1)
                    throw new Exception();
                if (date < StartTime)
                {
                    throw new Exception("Out of range");
                }
                if (date >= EndTime)
                {
                    _Cursor = Records.Count - 1;
                    return;
                }

                if (_Cursor > -1 && date == Records[_Cursor].Time)
                    return;

                int lowerLimit = 0;
                int higherLimit = Records.Count - 1;
                int midpoint = 0;
                while (lowerLimit <= higherLimit)
                {
                    midpoint = (lowerLimit + higherLimit) / 2;
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
        }

        /// <summary>
        /// Sets the cursor to the nearest tick before  or exacty at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestBefore(int unixTime)
        {
            SeekNearestBefore(unixTime.ToDatetime());
        }

        /// <summary>
        /// Sets the cursor to the nearest tick after or exactly at provided time.
        /// If the provided time is higher or lower than the know prices range it will be set to the last tick or first tick
        /// </summary> 
        public void SeekNearestAfter(DateTime time)
        {
            lock (Locker)
            {
                var ind = Records.BinarySearch(time);
                if (ind > -1)
                    Cursor = ind;
                else
                    Cursor = ~ind;
                Debug.Assert(Cursor > -1);
            }
        }

        /// <summary>
        /// ritorna true se ci sono altri cosi in avanti cursore di uno in avanti
        /// </summary>
        public bool MoveNext()
        {
            lock (Locker)
            {
                if (Cursor == Records.Count - 1)
                    return false;
                Cursor++;
                Debug.Assert(Cursor > -1);
                return true;
            }
        }

        /// <summary>
        /// returns false if the serie is finished
        /// </summary>
        /// <returns></returns>
        public bool Previous()
        {
            lock (Locker)
            {
                if (Cursor < 1)
                    return false;
                Cursor--;
                Debug.Assert(Cursor > -1);
                return true;
            }
        }

        public void PositionPush()
        {
            PositionSaveStack.Push(Cursor);
        }

        public void PositionPop()
        {
            Cursor = PositionSaveStack.Pop();
        }

        public TimeSerieTransform<T, TOut> ToSignal<TOut>(Func<T, TOut> selector) where TOut : IBaseData
        {
            return new TimeSerieTransform<T, TOut>(this, selector);
        }

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose()
        {
            lock (Locker)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Free managed objects 
                if (Records != null)
                {
                    Records.OnShrink -= this.OnRecordsShrunk;
                    Records.NewRecord -= Records_NewRecord;
                }
                Records = null;
            }

            disposed = true;
        }
    }

    public class TimeRecordCollection<T> where T : ITimeRecord
    {
        /// <summary>
        /// Signals that a new record was added to the collection
        /// </summary>
        public event Action<TimeRecordCollection<T>, T> NewRecord;

        /// <summary>
        /// Signals that the collection was shrunk, second parameter indicates elements count before shrink
        /// Warning: you should NOT use methods that read or write the collection to avoid a thread lock
        /// </summary>
        public event Action<TimeRecordCollection<T>, int> OnShrink;

        private List<T> Items;
        private ReaderWriterLock Lock = new ReaderWriterLock();

        public int Count => Items.Count;

        public TimeRecordCollection() => Items = new List<T>();

        public TimeRecordCollection(int capacity) => Items = new List<T>(capacity);

        public TimeRecordCollection(IEnumerable<T> items) => Items = items.ToList();

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
            NewRecord?.Invoke(this, item);
        }
        internal void Insert(int index, T item)
        {
            WriteOperation(() => Items.Insert(index, item));
        }
        public void AddRange(IEnumerable<T> items)
        {
            WriteOperation(() => Items.AddRange(items));
            NewRecord?.Invoke(this, items.Last());
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
            if (Items.Count < 1)
                return -1;
            if (time > Items[Items.Count - 1].Time)
                return ~Items.Count;
            if (time < Items[0].Time)
                return ~0;

            var lower = -1;
            var upper = Items.Count;

            while (lower < upper - 1)
            {
                var middle = lower + ((upper - lower) / 2);
                var compareResult = Items[middle].Time.CompareTo(time);

                if (compareResult == 0)
                    return middle;
                if (compareResult > 0)
                    upper = middle;
                else
                    lower = middle;
            }

            return ~upper;
        }

        internal void Shrink(int recordsMax)
        {
            if (this.Count > recordsMax)
            {
                WriteOperation(() =>
                {
                    var old = Items;
                    var oldCOunt = Items.Count;
                    Items.RemoveRange(0, Items.Count - recordsMax);
                    this.OnShrink?.Invoke(this, oldCOunt);
                });

            }
        }

        internal List<T> ToList()
        {
            return this.Items.ToList();
        }
    }
}
