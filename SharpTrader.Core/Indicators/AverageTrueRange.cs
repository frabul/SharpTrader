using System;

namespace SharpTrader.Indicators
{
    public class AverageTrueRange : Indicator<ITradeBar, IndicatorDataPoint>
    {
        private RollingWindow<IndicatorDataPoint> TrueRanges;

        public override bool IsReady => TrueRanges.Count >= Period + 1;
        public int Period { get; private set; }
        public TrueRange TrueRange { get; }

        double RollingSum = 0;
        public AverageTrueRange(int period) : base("AverageTrueRange")
        {
            Period = period;
        }

        public AverageTrueRange(string name, int period, TimeSerieNavigator<ITradeBar> chart, DateTime warmUpTime) :
           base(name, chart, warmUpTime)
        {
            Period = period;
            TrueRange = new TrueRange($"{name} Companion");

        }

        protected override IndicatorDataPoint Calculate(ITradeBar input)
        {
            TrueRange.Update(input);
            if (TrueRange.IsReady)
            {
                TrueRanges.Add(TrueRange.Current);
                int stepsCnt = Math.Min(TrueRanges.Count, Period);
                RollingSum += TrueRange.Current.Value;
                if (TrueRanges.Count >= Period)
                {
                    RollingSum -= TrueRanges[Period].Value;
                }
                var res = new IndicatorDataPoint(input.Time, RollingSum / stepsCnt);
                return res;
            }
            return null;

        }

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
    }
}
