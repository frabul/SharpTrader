using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core
{
    abstract class SymbolFeedBoilerplate    
    {
        public event Action<ISymbolFeed> OnTick;
        private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
        private List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)> NewCandleSubscribers =
            new List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)>();

        private bool onTickPending = false;
        private object Locker = new object();

        protected List<DerivedChart> DerivedTicks = new List<DerivedChart>(20);


        public TimeSerie<ICandlestick> Ticks { get; set; } = new TimeSerie<ICandlestick>();

 

        public class DerivedChart
        {
            public TimeSpan Timeframe;
            public TimeSerie<ICandlestick> Ticks;
            public Candlestick FormingCandle;
        }



        public virtual TimeSerieNavigator<ICandlestick> GetNavigator(TimeSpan timeframe)
        {
            if (BaseTimeframe == timeframe)
            {
                return Ticks;
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
                return new TimeSerieNavigator<ICandlestick>(der.Ticks);
            }

        }

        protected void AddTickToDerivedChart(DerivedChart der, ICandlestick newCandle)
        {
            if (der.FormingCandle == null)
                der.FormingCandle = new Candlestick(newCandle, der.Timeframe);

            if (der.FormingCandle.CloseTime <= newCandle.OpenTime)
            {
                //old candle is formed
                der.Ticks.AddRecord(der.FormingCandle);
                der.FormingCandle = new Candlestick(newCandle, der.Timeframe);
            }
            else
            {
                der.FormingCandle.Merge(newCandle);
                if (der.FormingCandle.CloseTime < newCandle.CloseTime)
                {

                    der.Ticks.AddRecord(der.FormingCandle);
                    der.FormingCandle = null;

                }
            }
        }

        protected void RaisePendingEvents(ISymbolFeed sender)
        {
            if (onTickPending)
            {
                OnTick?.Invoke(sender);
                onTickPending = false;
            }
        }

        protected void SignalTick()
        {
            onTickPending = true;
        }

        public void SubscribeToNewCandle(IChartDataListener subscriber, TimeSpan timeframe)
        {
            lock (Locker)
            {
                var (_, subs) = NewCandleSubscribers.FirstOrDefault(el => el.Timeframe == timeframe);
                if (subs == null)
                    NewCandleSubscribers.Add((timeframe, subs = new List<WeakReference<IChartDataListener>>()));

                for (int i = 0; i < subs.Count; i++)
                {
                    if (!subs[i].TryGetTarget(out var obj))
                        subs.RemoveAt(i--);
                }

                if (!subs.Any(it => it.TryGetTarget(out var sub) && sub.Equals(subscriber)))
                {
                    subs.Add(new WeakReference<IChartDataListener>(subscriber));
                }
            }
        }


    }
}
