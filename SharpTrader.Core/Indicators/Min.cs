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
        //todo
        public Min(TimeSerieNavigator<T> signal, Func<T, double> valueSelector)
            : base("Min(serie)", signal, valueSelector)
        {

            CalculateAll();
        }

        public override bool IsReady => throw new NotImplementedException();

        protected override double Calculate()
        {
            var signalOut = GetSignalCursor() >= Period ? GetSignal(Period) : double.MaxValue;
            var signalIn = GetSignal(0);
            if (signalOut > Filtered.LastTick.Value) //we are losing a value 
                return signalIn < Filtered.LastTick.Value ? signalIn : Filtered.LastTick.Value;
            else if (signalIn < signalOut) //the minimum is going out of range, but signalIn is lower than min then signalIn IS the min
                return signalIn;
            else
            {
                //min is going out of range so we need to search again
                double min = double.MaxValue;
                for (int i = 0; i < Period; i++)
                {
                    if (GetSignal(i) < min)
                        min = GetSignal(i);
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
