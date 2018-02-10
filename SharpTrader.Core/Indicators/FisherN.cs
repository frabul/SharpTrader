using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
 
namespace SharpTrader.Indicators
{

    public class FisherN<T> : Indicator where T : ITimeRecord
    { 
        public int Period { get; } 
        private TimeSerie<FRecord> Filtered { get; } = new TimeSerie<FRecord>();


        private Normalize<T> Normalize { get; }
        private TimeSerieNavigator<FRecord> Normalized = new TimeSerieNavigator<FRecord>();
        //private List<double> MidValues = new List<double>();

        public override bool IsReady => Filtered.Count > Period;

        public FisherN(TimeSerieNavigator<T> signal, Func<T, double> valueSelector, int period) : base("FisherN")
        {
            Normalize = new Normalize<T>(signal, valueSelector, period);
            Normalized = Normalize.GetNavigator();
            Normalized.OnNewRecord += rec => CalculateAll();
            Filtered.AddRecord(new FRecord(DateTime.MinValue, 0));
            //MidValues = new List<double>() { 0 };
            CalculateAll();

        }
        double LastMidValue;

        private void CalculateAll()
        {
            while (Normalized.Next())
            {
                var normalizedTick = Normalized.Tick.Value;
           
                var newMidValue = 0.33 * normalizedTick + 0.67 * LastMidValue;
                // MidValues.Add(midVal);
                var val = Fisher(newMidValue) + 0.5 * Filtered.LastTick.Value;
                Filtered.AddRecord(new FRecord(Normalized.Tick.Time, val));
                LastMidValue = newMidValue;
            }
        }


        double Fisher(double signalIn)
        {
            var v = signalIn < -0.998 ? -0.998 : (signalIn > 0.998 ? 0.998 : signalIn);
            return 0.5 * Math.Log((1d + v) / (1d - v));
        }

        public TimeSerieNavigator<FRecord> GetNavigator()
        {
            return new TimeSerieNavigator<FRecord>(Filtered);
        }
    }


}