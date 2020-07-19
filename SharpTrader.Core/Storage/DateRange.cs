using System;

namespace SharpTrader.Storage
{
    class DateRange
    {
        public DateTime start;
        public DateTime end;

        public DateRange(DateTime start, DateTime end)
        {
            this.start = start;
            this.end = end;
        }
        public bool Overlaps(DateTime start, DateTime end)
        {
            return start >= this.start || start <= this.end || end >= this.start || end <= this.end;
        }
    }
}
