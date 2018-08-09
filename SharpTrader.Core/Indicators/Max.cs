using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Max<T> : Filter<T> where T : ITimeRecord
    {
        public int Period { get; }
        public Max(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int period)
            : base("Max(serie)", signal, valueSelector)
        {
            Period = period;
            CalculateAll();
        }

        public override bool IsReady => Filtered.Count > Period;

        protected override double Calculate()
        {
            var sin = GetSignal(0);
            var sout = GetSignalCursor() >= Period ? GetSignal(Period) : double.MinValue;
            if (Filtered.Count < 1)
                return sin;
            else if (sout < Filtered.LastTick.Value)
                //if the sample that's going out of range is NOT the current max then we only need to check if the new sample is higher than current max
                return sin > Filtered.LastTick.Value ? sin : Filtered.LastTick.Value;
            else if (sin > sout)
                //the MAX is going out of range, but signalIn is higher than old max then signalIn IS the new MAX
                return sin;
            else
            {
                //sample that was the old max is going out of range so we need to search again
                double max = double.MinValue;
                var steps = Math.Min(GetSignalCursor() + 1, Period);
                for (int i = 0; i < steps; i++)
                {
                    var s = GetSignal(i);
                    if (s > max)
                        max = s;
                }
                return max;
            }

        }

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
    }
}
