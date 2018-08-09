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

        protected override double Calculate()
        {
            var sout = GetSignalCursor() >= Period ? GetSignal(Period) : double.MaxValue;
            var sin = GetSignal(0);

            if (Filtered.Count < 1)
                return sin;
            else if (sout > Filtered.LastTick.Value)
                //if the sample that's going out of range is NOT the current min then we only need to check if the new sample is lower than current min
                return sin < Filtered.LastTick.Value ? sin : Filtered.LastTick.Value;
            else if (sin < sout)
                //the current minimum is going out of range, but signalIn is lower than min then signalIn IS the new min
                return sin;
            else
            {
                //min is going out of range so we need to search again
                double min = double.MaxValue;
                var steps = Math.Min(GetSignalCursor() + 1, Period);
                for (int i = 0; i < steps; i++)
                {
                    var s = GetSignal(i);
                    if (s < min)
                        min = s;
                }
                return min;
            }

        }

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
    }


}
