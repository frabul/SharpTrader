using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public abstract class Indicator
    {
        public string Name { get; private set; }
        public Indicator(string name)
        {
            Name = name;
        }
        public abstract bool IsReady { get; }
    }

    public abstract class Filter<T> : Indicator where T : ITimeRecord
    {
        private TimeSerieNavigator<T> Signal;
        private Func<T, double> Selector;
        protected TimeSerie<FRecord> Filtered { get; } = new TimeSerie<FRecord>();

        public Filter(string name, TimeSerieNavigator<T> signal, Func<T, double> valueSelector) : base(name)
        {
            Signal = new TimeSerieNavigator<T>(signal);
            Signal.OnNewRecord += rec => CalculateAll();
            Selector = valueSelector;
            CalculateAll();
        }



        public TimeSerieNavigator<FRecord> GetNavigator() => new TimeSerieNavigator<FRecord>(Filtered);

        public FRecord this[int i] { get => Filtered.GetFromLast(i); }

        public int Count => Filtered.Count;

        public double Peek(double nextSignalSample)
        {
            var nextFilter = Calculate(nextSignalSample);
            return nextFilter;
        }

        protected void CalculateAll()
        {
            while (Signal.Next())
            {

                var value = Calculate();
                var time = Signal.Tick.Time;
                var rec = new FRecord(time, value);
                Filtered.AddRecord(rec);
            }
        }

        protected abstract double Calculate();

        protected abstract double Calculate(double sample);

        protected double GetSignal(int ind) => Selector(Signal.GetFromCursor(ind));

        protected int GetSignalCursor()
        {
            return Signal.Position;
        }
    }

    public struct FRecord : ITimeRecord
    {
        public DateTime Time { get; set; }
        public double Value { get; set; }
        public FRecord(DateTime time, double value)
        {
            Value = value;
            Time = time;
        }
    }
}
