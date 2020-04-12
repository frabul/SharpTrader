using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{

    public class FisherN<T> : Indicator<T, IndicatorDataPoint> where T : IBaseData
    {
        public int Period { get; }
        private Normalize<T> Normalize { get; }
        //private List<double> MidValues = new List<double>();
        IndicatorDataPoint LastValue;
        double LastMidValue;
        public override bool IsReady => SamplesCount > Period;

        public FisherN(string name, int period) : base(name)
        {
            Normalize = new Normalize<T>($"{name} Companion", period);
            LastValue = IndicatorDataPoint.Zero;
        }

        double Fisher(double signalIn)
        {
            var v = signalIn < -0.998 ? -0.998 : (signalIn > 0.998 ? 0.998 : signalIn);
            return 0.5 * Math.Log((1d + v) / (1d - v));
        }

        protected override IndicatorDataPoint Calculate(T input)
        {
            Normalize.Update(input);
            var normalizedTick = Normalize.Current.Value;

            var newMidValue = 0.33 * normalizedTick + 0.67 * LastMidValue;
            // MidValues.Add(midVal);
            var val = Fisher(newMidValue) + 0.5 * LastValue.Value;
            LastMidValue = newMidValue;
            LastValue = new IndicatorDataPoint(Normalize.Current.Time, val);
            return LastValue;
        }

        public override void Reset()
        {
            Normalize.Reset();
            this.LastValue = null; 
            base.Reset();
        }
    }
}