using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class HighPass<T> : Filter<T> where T : ITimeRecord
    {
        public int CutoffPeriod { get; private set; }

        private double a;
        private double alpha1;
        private double b;
        public double c;
        private double alpha;
        public override bool IsReady => Filtered.Count > CutoffPeriod + 3;

        public HighPass(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int cutOffPeriod)
            : base("HighPass", signal, valueSelector)
        {
            //bisogna fare una timeserie navigator che ritorna FIlter.Record (ha il suo selector interno )
            CutoffPeriod = cutOffPeriod;

            Filtered.AddRecord(new FRecord(DateTime.MinValue, 0));
            Filtered.AddRecord(new FRecord(DateTime.MinValue.AddDays(1), 0));
            Filtered.AddRecord(new FRecord(DateTime.MinValue.AddDays(2), 0));
            a = (0.707d * 2 * Math.PI) / CutoffPeriod;
            alpha1 = 1d + (Math.Sin(a) - 1d) / Math.Cos(a);
            b = 1d - alpha1 / 2d;
            c = 1d - alpha1;
            alpha = (double)CutoffPeriod / (1 + CutoffPeriod);

        }


        override protected double Calculate()
        {
            // alpha1 = (Cosine(.707*360 / 48) + Sine (.707*360 / 48) - 1) / Cosine(.707*360 / 48);
            // HP = (1 - alpha1 / 2)*(1 - alpha1 / 2)*(Close - 2*Close[1] + Close[2]) + 2*(1 - alpha1)*HP[1] - (1 - alpha1)*(1 - alpha1)*HP[2];
            var value = 0d;
            if (GetSignalCursor() > 3)
            {
                value = alpha * (Filtered.GetFromLast(0).Value + GetSignal(0) - GetSignal(1));
                //b * b * (GetSignal(0) - 2 * GetSignal(1) + GetSignal(2))
                //+ 2 * c * Filtered.GetFromLast(1).Value
                //- c * c * Filtered.GetFromLast(2).Value;


            }
            return value;
        }

        protected override double Calculate(double sample)
        {
            var value = 0d;
            if (GetSignalCursor() > 3)
                value = alpha * (Filtered.GetFromLast(0).Value + sample - GetSignal(0));
            return value;
        }

    }
}
