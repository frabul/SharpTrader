using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Min : Indicator<IBaseData, IBaseData>  
    {
        private IBaseData LastOutput;
        private RollingWindow<IBaseData> Inputs;

        public int Period { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public Min(string name, int period)
            : base(name)
        {
            Inputs = new RollingWindow<IBaseData>(period);
            Period = period;
        }


        public override bool IsReady => SamplesCount >= Period;



        protected override IBaseData Calculate(IBaseData input)
        {
            Inputs.Add(input);
            IBaseData output;
            var oneRemoved = Inputs.Samples > Inputs.Size;
            if (Inputs.Count < 2)
                output = input;
            else if (!oneRemoved || Inputs.MostRecentlyRemoved.Low > LastOutput.Low)
                //if the sample that's going out of range is NOT the current min then we only need to check if the new sample is lower than current min
                output = input.Low < LastOutput.Low ? input : LastOutput;
            else if (input.Low <= Inputs.MostRecentlyRemoved.Low)
                //the current minimum is going out of range, but signalIn is lower than min then signalIn IS the new min
                output = input;
            else
            {
                //min is going out of window so we need to search again
                var inputs = Inputs.GetRawSamples();
                output = inputs[0];
                foreach (var rec in inputs)
                {
                    if (rec.Low < output.Low)
                        output = rec;
                } 
            }
            Debug.Assert(output.Time >= input.Time - TimeSpan.FromMinutes(Period + 1));
            output = new IndicatorDataPoint(input.Time, output.Low);
            LastOutput = output;
            return output;
        }
        public override void Reset()
        {
            this.Inputs.Reset();
            base.Reset();
        }
    }


}
