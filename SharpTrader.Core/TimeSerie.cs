using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{

    public class TimeSerie<T> : TimeSerieNavigator<T> where T : ITimeRecord
    {

        /// <summary>
        /// the smallest difference in seconds
        /// </summary>
        /// <param name="smallestFrame"></param>
        public TimeSerie() : base()
        {

        }
        /// <summary>
        /// Create a timeserie using the internal buffer of the provieded one
        /// </summary> 
        public TimeSerie(TimeSerie<T> toCopy) : base(toCopy)
        {

        }

        public void AddRecord(T historyRecord, bool scatteredOrder = false)
        {
             
            if (scatteredOrder)
            {
                int index = Records.BinarySearch(historyRecord.Time);
                if (index > -1)
                    Records[index] = historyRecord;
                else
                {
                    Records.Add(historyRecord);
                } 
            }
            else
            {
                if (Records.Count > 0 && LastTickTime > historyRecord.Time)
                    throw new Exception("you cannot add a tick that's preceding the last one ");
                Records.Add(historyRecord);
            }


        }

        public T GetLast(Func<T, bool> criteria)
        {
            this.PositionPush();
            this.SeekNearestBefore(LastTickTime);
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

        internal void Shrink(int recordsCount)
        {
            Records.Shrink(recordsCount); 
        }
    }
}
