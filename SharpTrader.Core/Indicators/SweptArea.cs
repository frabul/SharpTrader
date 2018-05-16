using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class SweptArea : Indicator
    {
        public SweptArea(int steps, TimeSerieNavigator<ICandlestick> candles) : base("SweptArea")
        {
            Steps = steps;
            Candles = new TimeSerieNavigator<ICandlestick>(candles);
            candles.OnNewRecord += r => { CalculateAll(); };
            CalculateAll();
        }

        public int Steps { get; set; }

        private TimeSerieNavigator<ICandlestick> Candles;
        private TimeSerie<FRecord> Values = new TimeSerie<FRecord>();
        public override bool IsReady => Candles.Count > Steps;

        public double Value => Values.LastTick.Value;

        private void CalculateAll()
        {
            while (Candles.Next())
                Values.AddRecord(Calculate());
        }

        protected FRecord Calculate()
        {
            if (Candles.Position < Steps)
                return new FRecord() { Time = Candles.Tick.Time, Value = 0 };
            double min = double.MaxValue;
            double max = double.MinValue;
            double swept = 0;
            foreach (var candle in Enumerable.Range(0, Steps).Select(i => Candles.GetFromCursor(i)))
            {
                min = Math.Min(min, candle.Low);
                max = Math.Max(max, candle.High);
                swept += candle.High - candle.Low;
            }
            var area = (max - min) * Steps;
            var ret = swept / area;
            return new FRecord() { Time = Candles.Tick.Time, Value = ret };
        }


    }
}
