namespace SharpTrader.Indicators
{
    public class NormalizeToCurrentValue : Indicator<IBaseData, IBaseData>
    {
        private Indicator<IBaseData, IBaseData> BaseIndicator;
        private IBaseData LastInput;
        public override bool IsReady => BaseIndicator.IsReady;
        public NormalizeToCurrentValue(string name, Indicator<IBaseData, IBaseData> toNormalize) : base(name)
        {
            BaseIndicator = toNormalize; 
        }
         
        protected override IBaseData Calculate(IBaseData input)
        {
            LastInput = input;
            BaseIndicator.Update(input);
            return new IndicatorDataPoint(input.Time, BaseIndicator.Current.Value / input.Value);
        }

        protected override double CalculatePeek(double sample)
        {
            return BaseIndicator.Peek(sample) / LastInput.Value; 
        }

        public override void Reset()
        {
            LastInput = null;
            BaseIndicator.Reset();
            base.Reset();
        }
    }

}
