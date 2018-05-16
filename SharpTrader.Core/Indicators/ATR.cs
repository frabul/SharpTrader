using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class TrueRange<T> : Indicator where T : ICandlestick
    {

        TimeSerieNavigator<T> Candles;
        TimeSerie<FRecord> TrueRanges = new TimeSerie<FRecord>();


        public override bool IsReady => Candles.Count > 1;

        public TrueRange(TimeSerieNavigator<T> signal) : base("ATR")
        {

            Candles = new TimeSerieNavigator<T>(signal);
            Calculate();
            Candles.OnNewRecord += r => Calculate();
        }
        public TimeSerie<FRecord> GetNavigator()
        {
            return new TimeSerie<FRecord>(TrueRanges);
        }
        private void Calculate()
        {
            while (Candles.Next())
            {
                if (Candles.Position > 1)
                {
                    ICandlestick candle = Candles.Tick;
                    ICandlestick previous = Candles.PreviousTick;
                    var tr = Math.Max(Math.Max(candle.High - candle.Low, candle.High - previous.Close), previous.Close - candle.Low);
                    TrueRanges.AddRecord(new FRecord(candle.Time, tr));
                }
            }

        }
    }

    public class AverageTrueRange<T> : Indicator where T : ICandlestick
    {
        private TimeSerie<FRecord> TrueRanges;

        public override bool IsReady => TrueRanges.Count >= Steps;
        public int Steps { get; private set; }
        public double Value { get; private set; }


        public AverageTrueRange(TimeSerieNavigator<T> signal, int steps) : base("AverageTrueRange")
        {
            Steps = steps;
            var trueRange = new TrueRange<T>(signal);
            TrueRanges = trueRange.GetNavigator();

            Calculate();
            TrueRanges.OnNewRecord += nr => Calculate();
        }

        double RollingSum = 0;

        private void Calculate()
        {
            while (TrueRanges.Next())
            {
                int stepsCnt = Math.Min(TrueRanges.Count, Steps);

                RollingSum += TrueRanges.Tick.Value; 
                if (TrueRanges.Position >= Steps)
                {
                    RollingSum -= TrueRanges.GetFromCursor(Steps).Value;
                }
                Value = RollingSum / stepsCnt;
            }
        }
    }
}
