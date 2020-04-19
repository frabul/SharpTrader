namespace SharpTrader.Indicators
{
    public class Mean : Indicator<IBaseData, IBaseData>
    {
        private double _rollingSum; 
        public int Period { get; private set; }
        private RollingWindow<IBaseData> Inputs;
        /// <summary>
        /// Gets a flag indicating when this indicator is ready and fully initialized
        /// </summary>
        public override bool IsReady => SamplesCount >= Period;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mean"/> class using the specified name and period.
        /// </summary>  
        public Mean(string name, int period) : base(name)
        {
            Period = period;
            Inputs = new RollingWindow<IBaseData>(Period);
        }

        protected override IBaseData Calculate(IBaseData input)
        {
            Inputs.Add(input);
            _rollingSum += input.Value; 

            //remove the sample that's exiting the window from the rolling sum
        
            if (Inputs.Samples > Inputs.Size)
            {
                var valueToRemove = Inputs.MostRecentlyRemoved.Value;
                _rollingSum -= valueToRemove; 
            }
             
            //inputs has been set as Period + 1 so max size is number of sampes + 1
            var mean = _rollingSum / Inputs.Count; 
             
            return new IndicatorDataPoint(input.Time, mean);
        }

        public override void Reset()
        {
            this.Inputs.Reset();
            this._rollingSum = 0; 
            base.Reset();
        }
    }
}
