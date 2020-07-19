using System.Collections.Generic;
using LiteDB;

namespace SharpTrader.Storage
{
    class SymbolHistoryMetaData
    {
        [BsonId]
        public string Id { get; }
        public HistoryInfo Info { get; }
        public List<HistoryFileInfo> Chunks { get; }
        public List<DateRange> CoveredRanges { get; }
        public ITradeBar FirstBar { get; set; }
        public ITradeBar LastBar { get; set; }

        public SymbolHistoryMetaData(HistoryFileInfo histInfo)
        {
            Info = histInfo;
            Id = histInfo.GetKey();
            Chunks = new List<HistoryFileInfo>();
            CoveredRanges = new List<DateRange>();
        }
    }
}
