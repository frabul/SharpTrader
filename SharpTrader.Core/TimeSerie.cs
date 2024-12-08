﻿using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public TimeSerie(IEnumerable<T> records) : base(records)
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
                    if (Records.Count > 0 && EndTime > historyRecord.Time) 
                    {
                        //throw new Exception("you cannot add a tick that's preceding the last one ");
                        var insertIndex = ~index;
                        Records.Insert(insertIndex, historyRecord);

                        if (insertIndex > 0)
                            Debug.Assert(this.Records[insertIndex].Time > this.Records[insertIndex - 1].Time);
                        if (insertIndex + 1 < this.Records.Count)
                            Debug.Assert(this.Records[insertIndex].Time < this.Records[insertIndex + 1].Time);
                    }
                    else
                        Records.Add(historyRecord);
                }
            }
            else
            {
                if (Records.Count > 0 && EndTime > historyRecord.Time)
                    throw new Exception("you cannot add a tick that's preceding the last one ");
                Records.Add(historyRecord);
            }


        }

        public T GetLast(Func<T, bool> criteria)
        {
            this.PositionPush();
            this.SeekNearestBefore(EndTime);
            var res = default(T);
            while (this.Previous())
                if (criteria(this.Current))
                {
                    res = this.Current;
                    break;
                }
            this.PositionPop();
            return res;
        }

        internal void Shrink(int recordsCount)
        {
            Records.Shrink(recordsCount);
        }

        internal List<T> ToList()
        {
            return Records.ToList();
        }
    }
}
