using System;
using System.Collections.Generic;
using System.Text;

namespace SharpTrader.Indicators
{
    /// <summary>
    /// Exponential moving average
    /// </summary>
    public class EMA<T> : Indicator<T, IndicatorDataPoint> where T : IBaseData
    {
        public int Period { get; set; }
        private double Alpha;
        private Func<T, double> ValueSelector;
        private IndicatorDataPoint LastOutput;
        public override bool IsReady => Samples >= Period;
        public EMA(int emaPeriod,  Func<T, double> valueSelector, TimeSerieNavigator<T> signal, DateTime warmUpTime) 
            : base("EMA", signal, warmUpTime)
        {
            ValueSelector = valueSelector;
            Period = emaPeriod;
            Alpha = 1d / emaPeriod;
        }
         
        protected override IndicatorDataPoint CalculatePeek(double sample)
        {
            var signal = sample;
            var last = LastOutput.Value;
            return new IndicatorDataPoint(DateTime.MinValue, last + Alpha * (signal - last));
        }

        protected override IndicatorDataPoint Calculate(T input)
        { 
            if(LastOutput == null)
            {
                LastOutput = new IndicatorDataPoint(input.Time, input.Value);
            }
            else
            {
                var signal = ValueSelector(input);
                var last = LastOutput.Value;
                LastOutput = new IndicatorDataPoint(input.Time, last + Alpha * (signal - last));
            }
            return LastOutput;
        }
    }

}
