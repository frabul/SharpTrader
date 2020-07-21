using System;

namespace SharpTrader.Storage
{
    public class SymbolHistoryId
    {
        private string mask;
        private TimeSpan _TimeFrame;

        public string Symbol { get; private set; }
        public string Market { get; private set; }
        public TimeSpan Timeframe { get => _TimeFrame; set { _TimeFrame = value; SetMask(); } }
   
        private void SetMask()
        {
            mask = $"{this.Market}_{this.Symbol}_{(int)this.Timeframe.TotalMilliseconds}";
        }

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public SymbolHistoryId()
        {

        }

        public SymbolHistoryId(string market, string symbol, TimeSpan frame)
        {
            this.Market = market;
            this.Symbol = symbol;
            this.Timeframe = frame;
            SetMask();
        }

        internal string GetKey()
        {
            SetMask();
            return mask;
        }
    }

}
