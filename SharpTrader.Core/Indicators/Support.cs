using System;
using System.Collections.Generic;

namespace SharpTrader.Indicators
{
    public class Support : Indicator<IBaseData, IBaseData>
    {
        public int ClearanceSteps { get; }
        List<IBaseData> _Supports = new List<IBaseData>();
        public IReadOnlyList<IBaseData> List => _Supports;
        public Min MinIndicator { get; private set; }
        private IBaseData CurrentMin = new IndicatorDataPoint(DateTime.MinValue, 0);
        int CurrentMinLifeSpan = 0;


        public Support(string name, int clearanceSteps) : base(name)
        {
            ClearanceSteps = clearanceSteps;

            MinIndicator = new Min($"{name}_Min", clearanceSteps + 1);
        }

        public override bool IsReady => Current != null;

        protected override IBaseData Calculate(IBaseData input)
        {

            MinIndicator.Update(input);
            if (MinIndicator.Current.Time == CurrentMin.Time)
                CurrentMinLifeSpan++;
            else
            {
                CurrentMinLifeSpan = 0;
                CurrentMin = MinIndicator.Current;
            }
            if (CurrentMinLifeSpan == ClearanceSteps)
            {
                _Supports.Add(CurrentMin);
                return CurrentMin;
            }
            else
                return Current;
        }

        public override void Reset()
        {
            MinIndicator.Reset();
            _Supports.Clear();
            CurrentMinLifeSpan = 0;
            base.Reset();
        }
    }
}
