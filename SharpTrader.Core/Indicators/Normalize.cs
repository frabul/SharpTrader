using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Normalize<T> : Filter<T> where T : ITimeRecord
    {
        public int Period { get; }
        public Normalize(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int period)
            : base("Normalize", signal, valueSelector)
        {
            Period = period;
            CalculateAll();
        }

        public override bool IsReady => this.Filtered.Count >= Period;

        protected override double Calculate()
        {
            var len = GetSignalLen() - 1;
            if (len + 1 < Period)
                return 0;

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < Period; i++)
            {
                var val = GetSignal(i);
                if (val > max)
                    max = val;
                if (val < min)
                    min = val;
            }
            if (max > min)
                return 2d * (GetSignal(0) - min) / (max - min) - 1;
            else
                return 0;
        }

        protected override double Calculate(double sample)
        {
            throw new NotImplementedException();
        }
    }

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
            var signalOut = GetSignalLen() >= Period ? GetSignal(Period) : double.MaxValue;
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

        protected override double Calculate(double sample)
        {
            throw new NotImplementedException();
        }
    }

    public class Max<T> : Filter<T> where T : ITimeRecord
    {
        public int Period { get; }
        public Max(TimeSerieNavigator<T> signal, Func<T, double> valueSelector)
            : base("Max(serie)", signal, valueSelector)
        {

            CalculateAll();
        }

        public override bool IsReady => throw new NotImplementedException();

        //todo
        protected override double Calculate()
        {
            var signalOut = GetSignalLen() >= Period ? GetSignal(Period) : double.MinValue;
            var signalIn = GetSignal(0);
            if (signalOut < Filtered.LastTick.Value)
                return signalIn < Filtered.LastTick.Value ? signalIn : Filtered.LastTick.Value;
            else if (signalIn > signalOut) //the MAX is going out of range, but signalIn is lower than min then signalIn IS the min
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

        protected override double Calculate(double sample)
        {
            throw new NotImplementedException();
        }
    }
}
