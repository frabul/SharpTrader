using System;
using System.Collections.Generic;

namespace SharpTrader.Indicators
{
    public class Resistance : Indicator<IBaseData, IBaseData>
    {

        List<IBaseData> _Resistances = new List<IBaseData>();

        public int ClearanceSteps { get; }

        public Max MaxIndicator { get; private set; }
        public IReadOnlyList<IBaseData> List => _Resistances;

        int CurrentMaxLifeSpan = 0;
        private IBaseData CurrentMax = new IndicatorDataPoint(DateTime.MinValue, 0);

        public Resistance(string name, int clearanceSteps) : base(name)
        {
            ClearanceSteps = clearanceSteps;
            MaxIndicator = new Max($"{name}_Max", clearanceSteps + 1);
        }

        public override bool IsReady => Current != null;

        protected override IBaseData Calculate(IBaseData input)
        {
            MaxIndicator.Update(input);
            if (MaxIndicator.Current.Time == CurrentMax.Time)
                CurrentMaxLifeSpan++;
            else
            {
                CurrentMax = MaxIndicator.Current;
                CurrentMaxLifeSpan = 0;
            }

            if (CurrentMaxLifeSpan == ClearanceSteps)
            {
                this._Resistances.Add(CurrentMax);
                return CurrentMax;
            }
            return Current;
        }

        public override void Reset()
        {
            MaxIndicator.Reset();
            _Resistances.Clear();
            CurrentMaxLifeSpan = 0;
            base.Reset();
        }
    }
}
