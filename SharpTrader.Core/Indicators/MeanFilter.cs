using System;

namespace SharpTrader.Indicators
{
    public class MeanFilter<T> : Filter<T> where T : ITimeRecord
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;



        public int Period { get; private set; }
        public MeanFilter(int period, string name, TimeSerieNavigator<T> signal, Func<T, double> valueSelector)
           : base(name, signal, valueSelector)
        {
            Period = period;
        }

        public override bool IsReady => this.Count > Period;

        protected override double Calculate()
        {
            var value = GetSignal(0);

            _rollingSum += value;
            _rollingSumOfSquares += value * value;

            if (Count < 2)
                return _rollingSum;

            int samples = Period;
            if (Period <= GetSignalCursor())
            {
                var valueToRemove = GetSignal(Period);
                _rollingSum -= valueToRemove;
                _rollingSumOfSquares -= valueToRemove * valueToRemove;
            }
            else
                samples = this.Count + 1;

            var mean = _rollingSum / samples;
            var meanOfSquares = _rollingSumOfSquares / samples;


            var variance = meanOfSquares - mean * mean;

            return mean;
        }

        protected override double CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }

        public static void Test()
        {
            TimeSerie<FRecord> ts = new SharpTrader.TimeSerie<FRecord>( );

            var meanFilter = new MeanFilter<FRecord>(5, "asd", new TimeSerieNavigator<FRecord>(ts), e => e.Value);
            var time = DateTime.MinValue;
            for (int i = 0; i < 20; i++)
            {
                ts.AddRecord(new FRecord(time.AddSeconds(1), i));
            }
        }
    }
}
