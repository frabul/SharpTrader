using System;
using System.Collections.Generic;
using System.Text;

namespace SharpTrader.Indicators
{
    /// <summary>
    /// Exponential moving average
    /// </summary>
    public class EMA<T> : Filter<T> where T : ITimeRecord
    {
        public int Period { get; set; }
        private double Alpha;

        public EMA(int emaPeriod, TimeSerieNavigator<T> signal, Func<T, double> valueSelector) : base("EMA", signal, valueSelector)
        {
            Period = emaPeriod;
            Alpha = 1d / emaPeriod;
        }

        public override bool IsReady => this.GetSignalCursor() > 0;

        protected override double Calculate()
        {
            if (this.Filtered.Count < 1)
                return this.GetSignal(0);

            var signal = this.GetSignal(0);
            var last = this.Filtered.LastTick.Value;
            return last + Alpha * (signal - last);
        }

        protected override double CalculatePeek(double sample)
        {
            var signal = sample;
            var last = this.Filtered.LastTick.Value;
            return last + Alpha * (signal - last);
        }
    }

}
