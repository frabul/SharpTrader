using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public abstract class SymbolFeedBoilerplate
    {
        public event Action<ISymbolFeed> OnTick;
        private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);


        private bool onTickPending = false;
        private object Locker = new object();

        private List<DerivedChart> DerivedTicks = new List<DerivedChart>(20);


        public TimeSerie<ICandlestick> Ticks { get; set; } = new TimeSerie<ICandlestick>();



        public class DerivedChart
        {
            public TimeSpan Timeframe;
            public TimeSerie<ICandlestick> Ticks;
            public Candlestick FormingCandle;
        }



        public virtual Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe)
        {

            if (BaseTimeframe == timeframe)
            {
                return Task.FromResult<TimeSerieNavigator<ICandlestick>>(Ticks);
            }
            else
            {
                var der = DerivedTicks.Where(dt => dt.Timeframe == timeframe).FirstOrDefault();
                if (der == null)
                {
                    der = new DerivedChart()
                    {
                        Timeframe = timeframe,
                        FormingCandle = null,
                        Ticks = new TimeSerie<ICandlestick>()
                    };

                    //we need to initialize it with all the data that we have
                    var tticks = new TimeSerieNavigator<ICandlestick>(this.Ticks);
                    while (tticks.Next())
                    {
                        var newCandle = tticks.Tick;
                        AddTickToDerivedChart(der, newCandle);

                    }
                    DerivedTicks.Add(der);
                }
                return Task.FromResult(new TimeSerieNavigator<ICandlestick>(der.Ticks));
            }
        }

        public async Task<TimeSerieNavigator<ICandlestick>> GetNavigatorAsync(TimeSpan timeframe, DateTime historyStartTime)
        {
            return await GetNavigatorAsync(timeframe);
        }

        private void AddTickToDerivedChart(DerivedChart der, ICandlestick newCandle)
        {
            if (der.FormingCandle == null)
            {
                //the open time should be a multiple of der.TimeFrame
                var opeTime = GetOpenTime(newCandle.OpenTime, der.Timeframe);
                if (der.Timeframe.Minutes != 0)
                    Debug.Assert(opeTime.Minute % der.Timeframe.Minutes == 0);
                der.FormingCandle = new Candlestick(opeTime, newCandle, der.Timeframe);
            }
            else if (der.FormingCandle.CloseTime <= newCandle.OpenTime)
            {
                //old candle is ended, the new candle is already part of the next one
                der.Ticks.AddRecord(der.FormingCandle);
                var opeTime = GetOpenTime(newCandle.OpenTime, der.Timeframe);
                if (der.Timeframe.Minutes != 0)
                    Debug.Assert(opeTime.Minute % der.Timeframe.Minutes == 0);
                der.FormingCandle = new Candlestick(opeTime, newCandle, der.Timeframe);
            }
            else
            {
                //the new candle is part of the forming candle
                der.FormingCandle.Merge(newCandle);
                if (der.FormingCandle.CloseTime <= newCandle.CloseTime)
                {
                    der.Ticks.AddRecord(der.FormingCandle);
                    der.FormingCandle = null;
                }
            }
        }


        private DateTime GetOpenTime(DateTime mid, TimeSpan timeFrame)
        {
            long tfMs = (long)Math.Floor(timeFrame.TotalMilliseconds);
            long timeNowMs = mid.Ticks / 10000;
            long resto = timeNowMs % tfMs;
            var toAdd = resto > 0 ? tfMs - resto : 0;
            return new DateTime((timeNowMs + toAdd) * 10000, mid.Kind);
        }

        protected void UpdateDerivedCharts(ICandlestick newCandle)
        {
            foreach (var der in DerivedTicks)
            {
                AddTickToDerivedChart(der, newCandle);
            }
        }

        public void RaisePendingEvents(ISymbolFeed sender)
        {
            if (onTickPending)
            {
                OnTick?.Invoke(sender);
                onTickPending = false;
            }
            //todo raise NewCandle events
        }

        protected void SignalTick()
        {
            onTickPending = true;
        }
    }
}
