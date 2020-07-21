using System;
using ProtoBuf;

namespace SharpTrader.Storage
{
    public class SymbolHistoryRawExt : SymbolHistoryRaw
    {
        [ProtoIgnore]
        public DateTime StartOfData { get; set; } = DateTime.MaxValue;
        public DateTime EndOfData { get; set; } = DateTime.MinValue;
        internal bool HistoryInfoEquals(SymbolHistoryId histInfo)
        {
            return this.Market == histInfo.Market && this.Symbol == histInfo.Symbol && this.Timeframe == histInfo.Timeframe;
        }

        public void UpdateBars(ITradeBar bar)
        {
            if (this.EndOfData < bar.Time)
                this.EndOfData = bar.Time;

            if (this.StartOfData > bar.Time)
                this.StartOfData = bar.Time;
        }
    }
}
