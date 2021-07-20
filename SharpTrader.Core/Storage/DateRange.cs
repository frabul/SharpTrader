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
       
    }
}
