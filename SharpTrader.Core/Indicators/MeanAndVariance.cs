using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    /// <summary>
    /// This indicator computes the n-period population variance.
    /// </summary>
    public class MeanAndVariance : Indicator
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;
        public int Period { get; private set; }
        private TimeSerie<Record> Records = new TimeSerie<Record>();
        private TimeSerieNavigator<ITradeBar> Chart;

        public struct Record : ITimeRecord
        {
            public DateTime Time { get; set; }
            public double Mean { get; set; }
            public double Variance { get; set; }
            public Record(DateTime time, double mean, double variance) : this()
            {
                Mean = mean;
                Variance = variance;
                Time = time;
            }
        }

        public TimeSerieNavigator<Record> GetNavigator()
        {
            return new TimeSerieNavigator<Record>(Records);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified period.
        /// </summary>  
        public MeanAndVariance(int period, TimeSerieNavigator<ITradeBar> chart) : this("Variance_" + period, period, chart)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public MeanAndVariance(string name, int period, TimeSerieNavigator<ITradeBar> chart) : base(name)
        {
            Chart = new TimeSerieNavigator<ITradeBar>(chart);
            Period = period;
            chart.OnNewRecord += rec => this.Calculate();
            this.Calculate();
        }

        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => Records.Count >= Period;

        public Record Value => Records.Count > 0 ? Records.LastTick : default(Record);

        private void Calculate()
        {
            while (Chart.Next())
            {
                var chartTick = Chart.Tick;
                var value = Chart.Tick.Close;

                _rollingSum += value;
                _rollingSumOfSquares += value * value;

                if (Chart.Count < 2)
                    return;

                var indexToRemove = Period;
                if (Chart.Position < indexToRemove)
                    indexToRemove = (int)Chart.Position + 1;

                if (indexToRemove == Period && indexToRemove <= Chart.Position)
                {
                    var valueToRemove = Chart.GetFromCursor(indexToRemove).Close;
                    _rollingSum -= valueToRemove;
                    _rollingSumOfSquares -= valueToRemove * valueToRemove;
                }

                var mean = _rollingSum / indexToRemove;
                var meanOfSquares = _rollingSumOfSquares / indexToRemove;


                var variance = meanOfSquares - mean * mean;

                Records.AddRecord(
                    new Record(chartTick.CloseTime, mean, variance)
                );
            }

        }


    }
}
