using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Record = SharpTrader.Indicators.Filter.Record;
namespace SharpTrader.Indicators
{

    public class FisherN : Indicator
    {

        public int Period { get; }
        private TimeSerieNavigator<ITimeRecord> Signal;
        private Func<ITimeRecord, double> Selector;
        private TimeSerie<Record> Filtered { get; } = new TimeSerie<Record>();


        private Normalize Normalize { get; }
        private TimeSerieNavigator<Record> Normalized = new TimeSerieNavigator<Record>();
        //private List<double> MidValues = new List<double>();

        public override bool IsReady => Filtered.Count > Period;

        public FisherN(TimeSerieNavigator<ITimeRecord> signal, Func<ITimeRecord, double> valueSelector, int period) : base("FisherN")    
        {
            Normalize = new Normalize(signal, valueSelector, period);
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