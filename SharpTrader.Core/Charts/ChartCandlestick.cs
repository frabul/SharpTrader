using System;

namespace SharpTrader.Charts
{
#pragma warning disable IDE1006 // Naming Styles
    public struct ChartCandlestick
    {
        public DateTime time;
        public decimal open;
        public decimal high;
        public decimal low;
        public decimal close;

        public ChartCandlestick(ITradeBar toCopy) : this()
        {
            open = (decimal)toCopy.Open;
            close = (decimal)toCopy.Close;
            high = (decimal)toCopy.High;
            low = (decimal)toCopy.Low;
            time = toCopy.CloseTime;

        }
    }
}
