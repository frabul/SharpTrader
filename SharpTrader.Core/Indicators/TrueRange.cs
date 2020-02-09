using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class TrueRange : Indicator<ITradeBar, IndicatorDataPoint>
    {
        private ITradeBar LastSample;
        public override bool IsReady => Samples > 1;
        public TrueRange(string name) : base(name) { }
        public TrueRange(string name, TimeSerieNavigator<ITradeBar> chart, DateTime warmUpTime) :
            base(name, chart, warmUpTime)
        { }

        protected override IndicatorDataPoint Calculate(ITradeBar input)
        {
            IndicatorDataPoint result = null;
            if (LastSample != null)
            {
                ITradeBar candle = input;
                var vals = new[] { candle.High - candle.Low, candle.High - LastSample.Close, LastSample.Close - candle.Low };
                var tr = vals.Max();
                result = new IndicatorDataPoint(input.Time, tr);
            }
            LastSample = input;
            return result;
        }
        
        public override void Reset()
        {
            LastSample = null;
            base.Reset();
        }
    }
}
