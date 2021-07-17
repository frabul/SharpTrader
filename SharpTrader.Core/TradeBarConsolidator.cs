using System;
#pragma warning disable CS1998
namespace SharpTrader
{
    public class TradeBarConsolidator
    {
        public TimeSpan Resolution { get; }
      
        private Candlestick FormingCandle = new Candlestick();

        DateTime BaseTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public event Action<ITradeBar> OnConsolidated;
        
        public TradeBarConsolidator(TimeSpan resolution)
        {
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
        
        public void Scan(DateTime timeNow)
        {
            if (!FormingCandle.IsDefault() && timeNow >= FormingCandle.Time)
            {
                OnConsolidated?.Invoke(FormingCandle);
                FormingCandle = default;
            }
        }

        private DateTime GetOpenTime(DateTime timeNow, TimeSpan timeFrame)
        {
            long resto = (timeNow - BaseTime).Ticks % timeFrame.Ticks;
            return new DateTime((timeNow.Ticks - resto), timeNow.Kind);
        }
        
        private void OnTradeBar(ITradeBar newCandle)
        {
            DateTime tclose = newCandle.Time;
            DateTime topen = newCandle.OpenTime;

            if (FormingCandle.IsDefault())
            {
                //tbere isn't any currently forming candle, create one using new candle as generator
                var opeTime = GetOpenTime(newCandle.CloseTime, Resolution);
                FormingCandle = new Candlestick(opeTime, newCandle, Resolution);
            }
            if (topen >= FormingCandle.CloseTime || tclose > FormingCandle.CloseTime)
            {
                //old candle is ended, the new candle is already part of the next one
                OnConsolidated?.Invoke(FormingCandle);
                //use new candle as generator
                var opeTime = GetOpenTime(newCandle.CloseTime, Resolution);
                FormingCandle = new Candlestick(opeTime, newCandle, Resolution);
            }
            else
            {
                //the new candle is part of the forming candle
                FormingCandle.Merge(newCandle);
                //check if candle is completed and emit it
                if (tclose == FormingCandle.Time)
                {
                    OnConsolidated?.Invoke(FormingCandle);
                    FormingCandle = new Candlestick();
                }
            }
        }

    }
}
