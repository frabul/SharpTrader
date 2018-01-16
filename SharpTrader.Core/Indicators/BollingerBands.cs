using System;
using System.Linq;

namespace SharpTrader.Indicators
{
    public class BollingerBands : Indicator
    {
        // (Closing price-EMA(previous day)) x multiplier + EMA(previous day)

        TimeSerie<Record> Ticks = new TimeSerie<Record>();
        TimeSerieNavigator<ICandlestick> Chart;
        MeanAndVariance MeanAndVariance;
        TimeSerieNavigator<MeanAndVariance.Record> MeanAndVarianceValues;

        public int Period { get; set; }
        public double Deviation { get; set; }

        public override bool IsReady => Ticks.Count >= Period;

        public TimeSerieNavigator<Record> GetNavigator()
        {
            return new TimeSerieNavigator<Record>(Ticks);
        }

        public BollingerBands(string name, int period, double deviation, TimeSerieNavigator<ICandlestick> data) : base(name)
        {
            MeanAndVariance = new MeanAndVariance(period, data);
            MeanAndVarianceValues = MeanAndVariance.GetNavigator();
            Period = period;
            Deviation = deviation;

            Chart = new TimeSerieNavigator<ICandlestick>(data);
        }

        public void Calculate()
        {
            MeanAndVariance.Calculate();
            while (MeanAndVarianceValues.Next())
            {
                var mav = MeanAndVarianceValues.Tick;
                this.Ticks.AddRecord(new Record(mav.Mean, Math.Sqrt(mav.Variance) * Deviation, mav.Time));
            }
        }

        public struct Record : ITimeRecord
        {
            public double Deviation { get; }
            public double Main { get; }
            public DateTime Time { get; }
            public double Top => Main + Deviation;
            public double Bottom => Main - Deviation;

            public Record(double mean, double deviation, DateTime time)
            {
                this.Main = mean;
                this.Deviation = deviation;
                Time = time;
            }


        }

        public void Calculate(int index)
        {


            //int passi = Steps;
            ////if (AvgOfAvg[index - 1] == null || AvgOfAvg[index - 1] == 0 || AvgOfAvg[index - 1] == double.NaN)
            //if (true && bands.Main.Count < passi * 2)
            //{
            //    double media = 0;

            //    for (int i = 0; i < passi; i++)
            //    {
            //        if (bands.Main[index - i] == double.NaN)
            //        {
            //            media = bands.Main[index];
            //            break;
            //        }
            //        media += bands.Main[index - i];
            //    }

            //    AvgOfAvg[index] = media / passi;


            //}
            //else
            //{
            //    AvgOfAvg[index] = AvgOfAvg[index - 1] + (bands.Main[index] - bands.Main[index - passi]) / passi;
            //}
            //double attualizzazione = (AvgOfAvg[index] - AvgOfAvg[index - 1]) * Steps / 2;


            //Bottom[index] = bands.Bottom[index] + attualizzazione;
            ////
            //Top[index] = bands.Top[index] + attualizzazione;
            //Main[index] = bands.Main[index] + attualizzazione;


        }
    }




}
