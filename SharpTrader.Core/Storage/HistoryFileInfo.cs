using System;

namespace SharpTrader.Storage
{
    class HistoryFileInfo : HistoryInfo
    {
        public DateTime StartDate;
        public string FilePath;
        public HistoryFileInfo(string filePath, string market, string symbol, TimeSpan frame, DateTime date) : base(market, symbol, frame)
        {
            FilePath = filePath;
            this.StartDate = date;
        }
        public HistoryFileInfo(string market, string symbol, TimeSpan frame, DateTime date) : base(market, symbol, frame)
        {
            this.StartDate = date;
        }
        public bool Equals(HistoryInfo info)
        {
            return this.market == info.market && this.symbol == info.symbol && this.Timeframe == info.Timeframe;
        }
        public string GetFileName()
        {
            return $"{market}_{symbol}_{(int)Timeframe.TotalMilliseconds}_{StartDate:yyyyMM}.bin";
        }
    }

}
