using System;
using ProtoBuf;

namespace SharpTrader.Storage
{
    class SymbolHistoryRawExt : SymbolHistoryRaw
    {
        [ProtoIgnore]
        public DateTime StartOfData { get; set; }
        public DateTime EndOfData { get; set; }
        internal bool HistoryInfoEquals(HistoryInfo histInfo)
        {
            return this.Market == histInfo.market && this.Symbol == histInfo.symbol && this.Timeframe == histInfo.Timeframe;
        }
    }
}
