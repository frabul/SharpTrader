using System;
using System.Linq;

namespace SharpTrader.Indicators
{
    public class BollingerBandsRecord : IBaseData
    {
        public double Deviation { get; }
        public double Main { get; }
        public DateTime Time { get; }
        public double Top => Main + Deviation;
        public double Bottom => Main - Deviation;

        public double Low => Main;

        public double High => Main;

        public double Value => Main;

        public MarketDataKind Kind => MarketDataKind.Tick;

        public BollingerBandsRecord(double mean, double deviation, DateTime time)
        {
            this.Main = mean;
            this.Deviation = deviation;
            Time = time;
        }
    }
    public class BollingerBands : Indicator<ITradeBar, BollingerBandsRecord>
    {
        MeanAndVariance MeanAndVariance;
        public int Period { get; set; }
        public double Deviation { get; set; }

        public override bool IsReady => SamplesCount >= Period;

        public BollingerBands(string name, int period, double deviation) : base(name)
        {
            MeanAndVariance = new MeanAndVariance($"{name} Companion", period);
            Period = period;
            Deviation = deviation;
        }


        public BollingerBands(string name, int period, double deviation, TimeSerieNavigator<ITradeBar> data, DateTime warmUpTime)
            : base(name, data, warmUpTime)
        {
            MeanAndVariance = new MeanAndVariance($"{name} Companion", period);
            Period = period;
            Deviation = deviation;
        }
         
        protected override BollingerBandsRecord Calculate(ITradeBar input)
        {

            MeanAndVariance.Update(input);
            if (MeanAndVariance.IsReady)
            {
                var mav = MeanAndVariance.Current;
                var res = new BollingerBandsRecord(mav.Mean, Math.Sqrt(mav.Variance) * Deviation, mav.Time);
                return res;
            }
            return null;

        }
         
        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }

        public override void Reset()
        {
            this.MeanAndVariance.Reset(); 
            base.Reset();
        }
    }




}
