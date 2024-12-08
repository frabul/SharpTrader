using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public interface IIndicator
    {
        IBaseData Current { get; }
        double Value { get; }
        void Update(IBaseData datum);
        bool IsReady { get; }
    }
    public abstract class Indicator<TIn, TOut> : IIndicator where TIn : IBaseData where TOut : IBaseData
    {
        NLog.Logger Log { get; }

        private TimeSerieNavigator<TIn> Signal;
        /// <summary>the most recent input that was given to this indicator</summary>
        private TIn _previousInput;

        /// <summary>
        /// Event handler that fires after this indicator is updated
        /// </summary>
        public event Action<Indicator<TIn, TOut>, TOut> Updated;

        public string Name { get; private set; }
        public abstract bool IsReady { get; }

        public virtual TOut Current { get; private set; }

        public virtual double Value => Current.Value;

        public virtual int SamplesCount { get; private set; }

        IBaseData IIndicator.Current => this.Current;

        public Indicator(string name)
        {
            Log = NLog.LogManager.GetLogger(name);
            Name = name;
        }

        public Indicator(string name, TimeSerieNavigator<TIn> signal, DateTime warmUpTime)
            : this(name)
        {
            Name = name;
            Signal = new TimeSerieNavigator<TIn>(signal);
            Signal.SeekNearestBefore(warmUpTime);
            while (Signal.MoveNext())
                this.Update(Signal.Current);

            signal.OnNewRecord += rec => Update(rec);
        }

        /// <summary>
        /// 
        /// </summary>
        public void WarmUp(IEnumerable<TIn> data)
        {
            foreach (var point in data)
            {
                this.Update(point);
            }
        }

        public void Update(TIn input)
        {
            if (_previousInput != null && input.Time <= _previousInput.Time)
            {
                // if we receive a time in the past, log and return
                Log.Error($"This is a forward only indicator: {Name} Input: {input.Time:u} Previous: {_previousInput.Time:u}. It will not be updated with this input.");
                return;
            }
            if (!ReferenceEquals(input, _previousInput))
            {
                // compute a new value and update our previous time 
                if (!(input is TIn))
                {
                    throw new ArgumentException($"IndicatorBase.Update() 'input' expected to be of type {typeof(TIn)} but is of type {input.GetType()}");
                }
                _previousInput = (TIn)input;
                Current = Calculate((TIn)input);
                SamplesCount++;
                // let others know we've produced a new data point
                Updated?.Invoke(this, Current);

            }
        }

        public double Peek(double nextSignalSample)
        {
            return CalculatePeek(nextSignalSample);
        }

        public virtual void Reset()
        {
            this.SamplesCount = 0;
        }
        protected abstract TOut Calculate(TIn input);

        protected virtual double CalculatePeek(double sample)
        {
            throw new NotSupportedException($"{ this.GetType().Name} indicator doesn't support peek.");
        }

        public void Update(IBaseData datum) => Update((TIn)datum);
    }
}
