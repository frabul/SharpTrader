using System;
using System.Diagnostics;
#pragma warning disable CS1998
namespace SharpTrader
{
    public class TradeBarConsolidator
    {
        public bool SynchOnStartOfDay { get; }
        public TimeSpan Resolution { get; }
        public Candlestick LastEmittedCandle { get; private set; } = new Candlestick();
        public TimeSpan FinalCandleUpdateTimeout { get; set; } = TimeSpan.FromSeconds(8);

        private Candlestick FormingCandle = new Candlestick();

        DateTime BaseTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public event Action<ITradeBar> OnConsolidated;

        public TradeBarConsolidator(TimeSpan resolution, bool synchOnStartOfDay = false)
        {
            this.SynchOnStartOfDay = synchOnStartOfDay;
            this.Resolution = resolution;
        }

        public void Update(IBaseData data)
        {
            if (data.Kind == MarketDataKind.TradeBar)
            {
                OnTradeBar(data as ITradeBar);
            }
            else if (data.Kind == MarketDataKind.QuoteTick)
            {
                throw new NotImplementedException();
            }
        }

        private void EmitCandle(ref Candlestick candle)
        {
            OnConsolidated?.Invoke(candle);
            LastEmittedCandle = candle;
            FormingCandle = new Candlestick();
        }

        private DateTime CalculateFirstOpenTime(DateTime timeNow, TimeSpan timeFrame)
        {
            var baseTime = BaseTime;
            if (SynchOnStartOfDay)
                baseTime = new DateTime(timeNow.Year, timeNow.Month, timeNow.Day, 0, 0, 0, timeNow.Kind);


            long resto = (timeNow - baseTime).Ticks % timeFrame.Ticks;
            return new DateTime((timeNow.Ticks - resto), timeNow.Kind);
        }

        private void OnTradeBar(ITradeBar newCandle)
        {
            //check if we need to emit last forming candle ( or some fillers )
            Scan(newCandle.Time);

            if (FormingCandle.IsDefault())
            {
                //there isn't any currently forming candle, create one using new candle as generator
                var opeTime = CalculateFirstOpenTime(newCandle.CloseTime, Resolution);
                FormingCandle.SetData(opeTime, Resolution, newCandle);
            }
            else
            {
                //the new candle is part of the forming candle
                FormingCandle.Merge(newCandle);
            }
            Debug.Assert(newCandle.OpenTime <= FormingCandle.CloseTime);
            Debug.Assert(newCandle.Time <= FormingCandle.CloseTime);


            //check if candle is last
            if (newCandle.Time == FormingCandle.Time)
            {
                EmitCandle(ref FormingCandle);
            }

        }
        public void Scan(DateTime timeNow)
        {
            //emit all missing candlesticks 
            if (!LastEmittedCandle.IsDefault())
            {
                var timeIter = LastEmittedCandle.Time + Resolution + FinalCandleUpdateTimeout;
                while (timeIter <= timeNow)
                {
                    //if we have no data about forming candle then create a filler
                    if (FormingCandle.IsDefault() || FormingCandle.Time < LastEmittedCandle.Time + Resolution)
                        FormingCandle = Candlestick.GetFiller(LastEmittedCandle);
                    EmitCandle(ref FormingCandle);

                    timeIter += Resolution;
                }
            }
        }

    }
}
