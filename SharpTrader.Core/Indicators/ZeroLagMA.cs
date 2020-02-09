using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Math;
namespace SharpTrader.Indicators
{
    public class ZeroLagMARecord : IBaseData
    {
        public DateTime Time { get; }
        public double ZMA { get; internal set; }
        public double Variance { get; internal set; }
        internal double MA { get; set; }
        public double StdDev { get; internal set; }
        public double Low => ZMA;
        public double High => ZMA;
        public double Value => ZMA;
        public MarketDataKind Kind => MarketDataKind.Tick;

        public ZeroLagMARecord(DateTime time)
        {
            ZMA = 0;
            Variance = 0;
            MA = 0;
            Time = time;
        }
    }

    /// <summary>
    /// This indicator computes the n-period population variance.
    /// </summary>
    public class ZeroLagMA : Indicator<ITradeBar, ZeroLagMARecord>
    {
        private double _rollingSum;
        private double _rollingSumOfSquares;
        public int Period { get; private set; } = 5;
        public int SlopeSmoothingSteps { get; set; } = 3;
        private RollingWindow<ITradeBar> Inputs;
        private RollingWindow<ZeroLagMARecord> Outputs;

        public ZeroLagMA(SymbolInfo symbol, int period ) :
           base($"ZLMA {symbol.Key} {period}" )
        { 
            Period = period;
            Inputs = new RollingWindow<ITradeBar>(Period);
            Outputs = new RollingWindow<ZeroLagMARecord>(Period);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified period.
        /// </summary>  
        public ZeroLagMA(SymbolInfo symbol, int period, TimeSerieNavigator<ITradeBar> chart, DateTime warmUpTime) :
            this($"ZLMA {symbol.Key} {period}", period, chart, warmUpTime)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public ZeroLagMA(string name, int period, TimeSerieNavigator<ITradeBar> chart, DateTime warmUpTime)
            : base(name, chart, warmUpTime)
        {
            Period = period;
            Inputs = new RollingWindow<ITradeBar>(Period);
            Outputs = new RollingWindow<ZeroLagMARecord>(Period);
        }

        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => Samples >= Period;


        protected override ZeroLagMARecord Calculate(ITradeBar input)
        {
            var chartTick = input;
            var valToAdd = input.Value;

            double valToRemove = 0;
            double sqrToRemve = 0;

            if (Period <= Inputs.Count)
            {
                var oldTick = Inputs[Period];
                valToRemove = oldTick.Close;
                var oldZma = Outputs[Period - 1].ZMA;
                var oldDiff = Max(Abs(oldTick.High - oldZma), Abs(oldTick.Low - oldZma));
                sqrToRemve = oldDiff * oldDiff;
            }

            _rollingSum += valToAdd;
            _rollingSum -= valToRemove;
            var ma = _rollingSum / Period;

            double slope = 0;
            if (Samples > SlopeSmoothingSteps)
            {
                slope += ma - Outputs[0].MA;
                slope += Enumerable
                    .Range(0, SlopeSmoothingSteps - 1)
                    .Sum(i => Outputs[i].MA - Outputs[i + 1].MA);
                slope /= SlopeSmoothingSteps;
            }
            var zma = ma + slope * (Period - 1) / 2;
            var diff = Max(Abs(chartTick.High - zma), Abs(chartTick.Low - zma));
            var diffSqr = diff * diff;

            _rollingSumOfSquares = _rollingSumOfSquares + diffSqr - sqrToRemve;

            var variance = _rollingSumOfSquares / Period;

            var record = new ZeroLagMARecord(chartTick.CloseTime)
            {
                MA = ma,
                ZMA = zma,
                Variance = variance,
                StdDev = Math.Sqrt(variance)
            };
            return record;
        }
 
        public override void Reset()
        {
            this.Outputs.Reset();
            this.Inputs.Reset();
            this._rollingSum = 0;
            this._rollingSumOfSquares = 0;
            base.Reset();
        }
    }
}
