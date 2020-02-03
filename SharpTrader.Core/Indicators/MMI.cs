using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class MarketMeannessIndex : Indicator<ITradeBar, IndicatorDataPoint>
    {
        public override bool IsReady => Samples >= Period;

        private RollingWindow<ITradeBar> Inputs;
        private int Period;

        public MarketMeannessIndex(string name, int period, TimeSerieNavigator<ITradeBar> chart) : base(name)
        {

            Period = period;
            Inputs = new RollingWindow<ITradeBar>(period);
        }


        protected override IndicatorDataPoint Calculate(ITradeBar input)
        {
            Inputs.Add(input);
            if (Inputs.Count >= Period)
            {
                var m = 0d;
                int nh = 0, nl = 0;
                for (int i = 1; i < Period; i++)
                {
                    var vi = Inputs[i].Value;
                    var vi1 = Inputs[i - 1].Value;
                    if (vi > m && vi > vi1)
                        nl++;
                    else if (vi < m && vi < vi1)
                        nh++;
                }
                var value = 100d * (nl + nh) / (Period - 1);
                return new IndicatorDataPoint(input.Time, value);
            }
            return IndicatorDataPoint.Zero;
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
