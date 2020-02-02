using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class HighPass<T> : Indicator<T, IndicatorDataPoint> where T : IBaseData
    {
        public int CutoffPeriod { get; private set; }
        private double alpha;
        private IndicatorDataPoint LastOutput;
        private T LastInput;

        public override bool IsReady => Samples > CutoffPeriod / 2;

        public HighPass(string name, int cutOffPeriod, TimeSerieNavigator<T> signal, DateTime warmUpTime)
            : base(name, signal, warmUpTime)
        {
            CutoffPeriod = cutOffPeriod;
            alpha = (double)CutoffPeriod / (1 + CutoffPeriod);
        }

        public HighPass(string name, int highPassPeriod)
            : base(name)
        { 
            this.CutoffPeriod = highPassPeriod;
        }

        protected override IndicatorDataPoint Calculate(T input)
        {
            // alpha1 = (Cosine(.707*360 / 48) + Sine (.707*360 / 48) - 1) / Cosine(.707*360 / 48);
            // HP = (1 - alpha1 / 2)*(1 - alpha1 / 2)*(Close - 2*Close[1] + Close[2]) + 2*(1 - alpha1)*HP[1] - (1 - alpha1)*(1 - alpha1)*HP[2];
            var value = 0d;
            if (LastOutput == null)
                value = alpha * (0 + input.Value - LastInput.Value);
            else
                value = alpha * (LastOutput.Value + input.Value - LastInput.Value);
            //b * b * (GetSignal(0) - 2 * GetSignal(1) + GetSignal(2))
            //+ 2 * c * Filtered.GetFromLast(1).Value
            //- c * c * Filtered.GetFromLast(2).Value; 
            LastOutput = new IndicatorDataPoint(input.Time, value);
            LastInput = input;
            return LastOutput;
        }

        protected override IndicatorDataPoint CalculatePeek(double sample)
        {
            double value = 0d;
            if (this.IsReady)
                value = alpha * (LastOutput.Value + sample - LastInput.Value);
            return new IndicatorDataPoint(DateTime.MinValue, value);
        }
    }
}
