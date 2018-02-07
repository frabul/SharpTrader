using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class MMI : Indicator
    {
        public override bool IsReady => throw new NotImplementedException();

        private TimeSerieNavigator<ICandlestick> Chart;
        private int Period;

        public MMI(string name, int period, TimeSerieNavigator<ICandlestick> chart) : base(name)
        {
            Chart = chart;
            Period = period;
        }

        private void Calculate()
        {
            var m = 0d;

            int nh = 0, nl = 0;
            for (int i = 1; i < Period; i++)
            {
                var vi = Chart.GetFromCursor(i).Close;
                var vi1 = Chart.GetFromCursor(i - 1).Close;
                if (vi > m && vi > vi1)
                    nl++;
                else if (vi < m && vi < vi1)
                    nh++;
            }
            var value = 100d * (nl + nh) / (Period - 1);
        }

    }
}
