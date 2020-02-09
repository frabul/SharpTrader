using System;

namespace SharpTrader.Indicators
{
    public class MeanAndViarianceFilter<T> : Indicator<T, MeanAndVarianceRecord> where T : IBaseData
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;
        public int Period { get; private set; }
        private RollingWindow<T> Inputs;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public MeanAndViarianceFilter(string name, int period) : base(name)
        {
            Inputs = new RollingWindow<T>(Period);
            Period = period;
        }

        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => Samples >= Period;

        protected override MeanAndVarianceRecord Calculate(T input)
        {
            //remove the sample that's exiting the window from the rolling sum
            if (Inputs.Count >= Period)
            {
                var valueToRemove = Inputs[Period - 1].Value;
                _rollingSum -= valueToRemove;
                _rollingSumOfSquares -= valueToRemove * valueToRemove;
            }

            Inputs.Add(input);
            _rollingSum += input.Value;
            _rollingSumOfSquares += input.Value * input.Value;

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
