using System;

namespace SharpTrader.Storage
{
    public class HistoryFileInfo  
    {
        public SymbolHistoryId HistoryId { get; private set; }
        public DateTime StartDate { get; private set; }
        public string FilePath { get; set; }
        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public HistoryFileInfo()
        {

        }

        public HistoryFileInfo(string filePath, SymbolHistoryId id, DateTime date) 
        {
            HistoryId = id;
            FilePath = filePath;
            this.StartDate = date;
        }
        public HistoryFileInfo(SymbolHistoryId id, DateTime date) 
        {
            HistoryId = id;
            this.StartDate = date;
        }
        public bool Equals(SymbolHistoryId info)
        {
            throw new NotImplementedException();
        }
        public override bool Equals(object obj)
        {
            throw new NotImplementedException();
        }
        public string GetFileName()
        {
            return $"{HistoryId.Market}_{HistoryId.Symbol}_{(int)HistoryId.Timeframe.TotalMilliseconds}_{StartDate:yyyyMM}.bin";
        }
    }

}
