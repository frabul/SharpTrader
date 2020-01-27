using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Math;
namespace SharpTrader.Indicators
{
    /// <summary>
    /// This indicator computes the n-period population variance.
    /// </summary>
    public class ZeroLagMA : Indicator
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;
        public int Period { get; private set; } = 5;
        public int SlopeSmoothingSteps { get; set; } = 3;
        private TimeSerie<Record> Records = new TimeSerie<Record>();
        private TimeSerieNavigator<ITradeBar> Chart;

        public class Record : ITimeRecord
        {
            public DateTime Time { get; internal set; }
            public double ZMA { get; internal set; }
            public double Variance { get; internal set; }
            internal double MA { get; set; }
            public double StdDev { get; internal set; }

            public Record(DateTime time)
            {
                ZMA = 0;
                Variance = 0;
                MA = 0;
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
        public ZeroLagMA(int period, TimeSerieNavigator<ITradeBar> chart) : this("Variance_" + period, period, chart)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public ZeroLagMA(string name, int period, TimeSerieNavigator<ITradeBar> chart) : base(name)
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

        private void Calculate()
        {
            while (Chart.Next())
            {
                var chartTick = Chart.Tick;
                var valToAdd = Chart.Tick.Close;


                double valToRemove = 0;
                double sqrToRemve = 0;
                if (Period < Chart.Count)
                {
                    var oldTick = Chart.GetFromCursor(Period);
                    valToRemove = oldTick.Close;
                    var oldZma = Records.GetFromLast(Period - 1).ZMA;
                    var oldDiff = Max(Abs(oldTick.High - oldZma), Abs(oldTick.Low - oldZma));
                    sqrToRemve = oldDiff * oldDiff;
                }
                _rollingSum += valToAdd;
                _rollingSum -= valToRemove;
                var ma = _rollingSum / Period;


                double slope = 0;
                if (Records.Count > SlopeSmoothingSteps)
                {
                    slope += ma - Records.GetFromLast(0).MA;
                    slope += Enumerable.Range(0, SlopeSmoothingSteps - 1).Sum(i => Records.GetFromLast(i).MA - Records.GetFromLast(i + 1).MA);
                    slope /= SlopeSmoothingSteps;
                }
                var zma = ma + slope * (Period - 1) / 2;
                var diff = Max(Abs(chartTick.High - zma), Abs(chartTick.Low - zma));
                var diffSqr = diff * diff;

                _rollingSumOfSquares = _rollingSumOfSquares + diffSqr - sqrToRemve;

                var variance = _rollingSumOfSquares / Period;

                var record = new Record(chartTick.CloseTime)
                {
                    MA = ma,
                    ZMA = zma,
                    Variance = variance,
                    StdDev = Math.Sqrt(variance)
                };
                Records.AddRecord(record);
            }

        }


    }
}
