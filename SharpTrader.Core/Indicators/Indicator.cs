﻿using System;
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
        protected TimeSerie<Record> Filtered { get; } = new TimeSerie<Record>();
        private TimeSpan halfSpan;
        public Filter(string name, TimeSerieNavigator<T> signal, Func<T, double> valueSelector) : base(name)
        {
            Signal = new TimeSerieNavigator<T>(signal);
            Signal.OnNewRecord += rec => CalculateAll();
            Selector = valueSelector;
        }

        public TimeSerieNavigator<Record> GetNavigator() => new TimeSerieNavigator<Record>(Filtered);

        protected void CalculateAll()
        {
            while (Signal.Next())
            {

                var value = Calculate();
                var time = Signal.Tick.Time;
                var rec = new Record(time, value);
                Filtered.AddRecord(rec);
            }
        }


        protected abstract double Calculate();

        protected double GetSignal(int ind) => Selector(Signal.GetFromCursor(ind));

        protected int GetSignalLen()
        {
            return Signal.Count;
        }

        public struct Record : ITimeRecord
        {
            public DateTime Time { get; set; }
            public double Value { get; set; }
            public Record(DateTime time, double value)
            {
                Value = value;
                Time = time;
            }
        }
    }
}
