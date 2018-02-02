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
            var signalOut = GetSignal(Period);
            var signalIn = GetSignal(0);
            if (signalOut > Filtered.LastTick.Value)
                return signalIn < Filtered.LastTick.Value ? signalIn : Filtered.LastTick.Value;
            else
                throw new NotImplementedException(""); //we need to search new min agian

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
            var signalOut = GetSignal(Period);
            var signalIn = GetSignal(0);
            if (signalOut < Filtered.LastTick.Value)
                return signalIn < Filtered.LastTick.Value ? signalIn : Filtered.LastTick.Value;
            else
                throw new NotImplementedException(); //we need to search again

        }
    }
}
