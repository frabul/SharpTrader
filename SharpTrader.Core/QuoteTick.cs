using System;

namespace SharpTrader
{
    public class QuoteTick : IBaseData
    {
        public double Bid { get; }
        public double Ask { get; }
        public DateTime Time { get; }

        public double Value => Bid;

        public MarketDataKind Kind => MarketDataKind.QuoteTick;

        public double Low => Bid;

        public double High => Bid;

        public QuoteTick(double bid, double ask, DateTime eventTime)
        {
            this.Bid = bid;
            this.Ask = ask;
            this.Time = eventTime;
        }
    }
}
