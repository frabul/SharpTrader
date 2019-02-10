using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Min<T> : Filter<T> where T : ITimeRecord
    {
        public int Period { get; }

        public Min(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int period)
            : base("Min(serie)", signal, valueSelector)
        {
            Period = period;
            CalculateAll();
        }

        public override bool IsReady => Filtered.Count >= Period;

        /// <summary>
        /// The record that generated the min 
        /// </summary>
        public FRecord Current { get; private set; }

        protected override double Calculate()
        {
            var sout = GetSignalCursor() >= Period ? GetSignal(Period) : double.MaxValue;
            var sin = GetSignalAndTime(0);

            if (Filtered.Count < 1)
                Current = sin;
            else if (sout > Filtered.LastTick.Value)
                //if the sample that's going out of range is NOT the current min then we only need to check if the new sample is lower than current min
                Current = sin.Value < Filtered.LastTick.Value ? sin : Current;
            else if (sin.Value < sout)
                //the current minimum is going out of range, but signalIn is lower than min then signalIn IS the new min
                Current = sin;
            else
            {
                //min is going out of range so we need to search again
                Current = new FRecord(default(DateTime), double.MaxValue);
                var steps = Math.Min(GetSignalCursor() + 1, Period);
                for (int i = 0; i < steps; i++)
                {
                    var rec = GetSignalAndTime(i);
                    if (rec.Value < Current.Value)
                        Current = rec;
                }

            }
            return Current.Value;
        }

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
    }


}
