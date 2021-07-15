using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class SweptArea : Indicator<IBaseData, IndicatorDataPoint>
    {
        public int Period { get; set; }

        private RollingWindow<IBaseData> Inputs;
        public override bool IsReady => Inputs.Count >= Period;
        public SweptArea(string name, int period)
            : base(name)
        {
            Period = period;
            Inputs = new RollingWindow<IBaseData>(period);
        }

        protected override IndicatorDataPoint Calculate(IBaseData input)
        {
            if (Inputs.Count < Period)
                return IndicatorDataPoint.Zero;

            double min = double.MaxValue;
            double max = double.MinValue;
            double swept = 0;
            foreach (var candle in Inputs)
            {
                min = Math.Min(min, candle.Low);
                max = Math.Max(max, candle.High);
                swept += candle.High - candle.Low;
            }
            var area = (max - min) * Period;
            var ret = swept / area;
            return new IndicatorDataPoint(input.Time, ret);
        }

        public override void Reset()
        {
            this.Inputs.Reset();
            base.Reset();
        }
    }
}
