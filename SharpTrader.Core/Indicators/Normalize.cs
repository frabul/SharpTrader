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
            var len = GetSignalCursor() - 1;
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

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
    }

    
}
