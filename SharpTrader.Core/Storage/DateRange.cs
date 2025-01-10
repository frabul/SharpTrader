using System;

namespace SharpTrader.Storage
{
    public class DateRange
    {
        public DateTime start;
        public DateTime end;

        public DateRange(DateTime start, DateTime end)
        {
            this.start = start;
            this.end = end;
        }
        public bool Overlaps(DateTime startDate, DateTime endDate)
        {
            return
                (this.start >= startDate && this.start < endDate) ||
                (this.end > startDate && this.end <= endDate) ||
                (startDate >= this.start && startDate < this.end);
        }
    }
}
