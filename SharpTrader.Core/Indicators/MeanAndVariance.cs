using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class MeanAndVarianceRecord : IBaseData
    {
        public DateTime Time { get; set; }
        public double Mean { get; set; }
        public double Variance { get; set; }

        public double Low => Mean;

        public double High => Mean;

        public double Value => Mean;

        public MarketDataKind Kind => MarketDataKind.Tick;

        public MeanAndVarianceRecord(DateTime time, double mean, double variance)
        {
            Mean = mean;
            Variance = variance;
            Time = time;
        }
    }
    /// <summary>
    /// This indicator computes the n-period population variance.
    /// </summary>
    public class MeanAndVariance : Indicator<ITradeBar, MeanAndVarianceRecord>
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;
        public int Period { get; private set; }
        private RollingWindow<ITradeBar> Inputs;
        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => Samples >= Period;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified period.
        /// </summary>  
        public MeanAndVariance(string name, int period, TimeSerieNavigator<ITradeBar> chart, DateTime warmUpTime)
            : base(name, chart, warmUpTime)
        {
            Inputs = new RollingWindow<ITradeBar>(Period);
            Period = period;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public MeanAndVariance(string name, int period) : base(name)
        {
            Inputs = new RollingWindow<ITradeBar>(Period);
            Period = period;
        }


        protected override MeanAndVarianceRecord Calculate(ITradeBar input)
        {
            _rollingSum += input.Value;
            _rollingSumOfSquares += input.Value * input.Value;

            //remove the sample that's exiting the window from the rolling sum
            if (Inputs.Count >= Period)
            {
                var valueToRemove = Inputs[Period - 1].Value;
                _rollingSum -= valueToRemove;
                _rollingSumOfSquares -= valueToRemove * valueToRemove;
            }
            Inputs.Add(input);

            //inputs has been set as Period + 1 so max size is number of sampes + 1
            var mean = _rollingSum / Inputs.Count;
            var meanOfSquares = _rollingSumOfSquares / Inputs.Count;


            var variance = meanOfSquares - mean * mean;
            return new MeanAndVarianceRecord(input.Time, mean, variance);
        }

        public override void Reset()
        {
            this.Inputs.Reset();
            this._rollingSum = 0;
            this._rollingSumOfSquares = 0;
            base.Reset();
        }
    }
}
