using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public class Min<T> : Indicator<T, T> where T : IBaseData
    {
        private T LastOutput;
        private RollingWindow<T> Inputs;

        public int Period { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeanAndVariance"/> class using the specified name and period.
        /// </summary>  
        public Min(string name, int period)
            : base(name)
        {
            Inputs = new RollingWindow<T>(period + 1);
            Period = period;
        }


        public override bool IsReady => Samples >= Period;



        protected override T Calculate(T input)
        {
            var sampleOut = Inputs.Count >= Period ? Inputs[Period - 1].Value : double.MaxValue;
            Inputs.Add(input);
            T output;

            if (Inputs.Count < 2)
                output = input;
            else if (sampleOut > LastOutput.Value)
                //if the sample that's going out of range is NOT the current min then we only need to check if the new sample is lower than current min
                output = input.Value < LastOutput.Value ? input : LastOutput;
            else if (input.Value < sampleOut)
                //the current minimum is going out of range, but signalIn is lower than min then signalIn IS the new min
                output = input;
            else
            {
                //min is going out of window so we need to search again
                output = Inputs[0];
                for (int i = 1; i < Inputs.Count; i++)
                {
                    var rec = Inputs[i];
                    if (rec.Value < output.Value)
                        output = rec;
                }
            }
            LastOutput = output;
            return output;
        }

        protected override T CalculatePeek(double sample)
        {
            throw new NotImplementedException();
        }
        public override void Reset()
        {
            this.Inputs.Reset();
            base.Reset();
        }
    }


}
