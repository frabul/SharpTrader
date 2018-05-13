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
        public Max(TimeSerieNavigator<T> signal, Func<T, double> valueSelector)
            : base("Max(serie)", signal, valueSelector)
        {

            CalculateAll();
        }

        public override bool IsReady => GetSignalCursor() > Period;

        protected override double Calculate()
        {
            var signalOut = GetSignalCursor() >= Period ? GetSignal(Period) : double.MinValue;
            var signalIn = GetSignal(0);
            if (signalOut < Filtered.LastTick.Value)
                return signalIn < Filtered.LastTick.Value ? signalIn : Filtered.LastTick.Value;
            else if (signalIn > signalOut) //the MAX is going out of range, but signalIn is higher than olde max then signalIn IS the new MAX
                return signalIn;
            else
            {
                //MAX is going out of range so we need to search again
                double max = double.MinValue;
                for (int i = 0; i < Period; i++)
                {
                    if (GetSignal(i) > max)
                        max = GetSignal(i);
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
