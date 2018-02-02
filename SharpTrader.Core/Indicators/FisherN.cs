using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Record = SharpTrader.Indicators.Filter<SharpTrader.ITimeRecord>.Record;
namespace SharpTrader.Indicators
{

    public class FisherN<T> : Indicator where T : ITimeRecord
    {

        public int Period { get; }
        private TimeSerieNavigator<ICandlestick> Signal;
        private Func<ICandlestick, double> Selector;
        private TimeSerie<Record> Filtered { get; } = new TimeSerie<Record>();


        private Normalize<T> Normalize { get; }
        private TimeSerieNavigator<Filter<T>.Record> Normalized = new TimeSerieNavigator<Filter<T>.Record>();
        //private List<double> MidValues = new List<double>();

        public override bool IsReady => Filtered.Count > Period;

        public FisherN(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int period) : base("FisherN")
        {
            Normalize = new Normalize<T>(signal, valueSelector, period);
            Normalized = Normalize.GetNavigator();
            Normalized.OnNewRecord += rec => CalculateAll();
            //MidValues = new List<double>() { 0 };
            CalculateAll();

        }
        double LastMidValue;

        private void CalculateAll()
        {
            while (Normalized.Next())
            {
                LastMidValue = 0.33 * Normalized.Tick.Value + 0.67 * LastMidValue;
                // MidValues.Add(midVal);
                var val = Fisher(LastMidValue) + 0.5 * Filtered.LastTick.Value;
                Filtered.AddRecord(new Record(Normalized.Tick.Time, val));
            }
        }


        double Fisher(double signalIn)
        {
            var v = signalIn < -0.998 ? -0.998 : (signalIn > 0.998 ? 0.998 : signalIn);
            return 0.5 * Math.Log((1d + v) / (1d - v));
        }
    }


}