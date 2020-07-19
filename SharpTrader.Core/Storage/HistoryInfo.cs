using System;

namespace SharpTrader.Storage
{
    public class SymbolHistoryId
    {
        public string symbol { get; private set; }
        public string market { get; private set; }
        public TimeSpan Timeframe { get => _TimeFrame; set { _TimeFrame = value; SetMask(); } }
        private string mask;
        private TimeSpan _TimeFrame;
        private void SetMask()
        {
            mask = $"{this.market}_{this.symbol}_{(int)this.Timeframe.TotalMilliseconds}";
        }
        public SymbolHistoryId(string market, string symbol, TimeSpan frame)
        {
            this.market = market;
            this.symbol = symbol;
            this.Timeframe = frame;
            SetMask();
        }
        internal string GetKey()
        {
            return mask;
        }
    }

}
