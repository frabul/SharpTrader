using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Normalize<T> : Indicator<T, IndicatorDataPoint> where T : IBaseData
    {
        RollingWindow<T> Inputs;
        public int Period { get; }
        public Normalize(string name, int period)
            : base(name)
        {
            Period = period;
            Inputs = new RollingWindow<T>(period);
        }

        public override bool IsReady => Samples >= Period;

        protected override IndicatorDataPoint Calculate(T input)
        {
            Inputs.Add(input);
            if (Inputs.Count < Period)
                return IndicatorDataPoint.Zero;

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < Period; i++)
            {
                var val = Inputs[i].Value;
                if (val > max)
                    max = val;
                if (val < min)
                    min = val;
            }

            if (max > min)
                return new IndicatorDataPoint(input.Time, 2d * (input.Value - min) / (max - min) - 1);
            else
                throw new Exception("Unexpected error in Normalize indicator");
        }

        protected override IndicatorDataPoint CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }

        public override void Reset()
        {
            this.Inputs.Reset();
            base.Reset();
        }
    }


}
