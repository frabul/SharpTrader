using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SharpTrader.Storage
{

    public interface HistoryChunkId
    {
        public SymbolHistoryId HistoryId { get; }
        DateTime StartDate { get; }

        DateTime EndDate { get; }
        string GetFilePath(string dataDir);
    }
    [ProtoContract]
    public class HistoryChunkIdV2 : HistoryChunkId
    {
        private DateTime startDate;

        [ProtoMember(1)]
        public SymbolHistoryId HistoryId { get; private set; }

        [ProtoMember(2)]
        public DateTime StartDate { get => startDate; private set => startDate = value.ToUniversalTime(); }

        public DateTime EndDate => StartDate.AddMonths(1);

        public string GetFilePath(string dataDir) => Path.Combine(dataDir, Key + ".bin2");

        public string Key => HistoryId.Key + $"_{StartDate:yyyyMM}";

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public HistoryChunkIdV2()
        {

        }

        public HistoryChunkIdV2(SymbolHistoryId id, DateTime date)
        {
            HistoryId = id;
            this.StartDate = date;
        }

        public override int GetHashCode()
        {
            return this.Key.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            var other = obj as HistoryChunkIdV2;
            return other != null && this.Key == other.Key;
        }

        public static HistoryChunkIdV2 Parse(string filePath)
        {
            HistoryChunkIdV2 ret = null;
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var lastDivisor = fileName.LastIndexOf("_");
                string key = fileName.Substring(0, lastDivisor);
                string datestr = fileName.Substring(lastDivisor + 1);
                var symId = SymbolHistoryId.Parse(key);
                var date = DateTime.ParseExact(datestr, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                ret = new HistoryChunkIdV2()
                {
                    HistoryId = symId,
                    StartDate = date
                };
            }
            catch (Exception _ex)
            {
                Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }

            return ret;

        }
        public override string ToString()
        {
            return this.Key;
        }
    }

    /// <summary>
    /// Data contained is > startDate and  <= endDate
    /// </summary>
    public class HistoryChunkIdV3 : HistoryChunkId
    {
        public SymbolHistoryId HistoryId { get; private set; }
        private DateTime startDate;
        private DateTime endDate;

        public string Symbol { get => HistoryId.Symbol; private set => HistoryId.Symbol = value; }

        public string Market { get => HistoryId.Market; private set => HistoryId.Market = value; }

        public TimeSpan Resolution { get => HistoryId.Resolution; private set => HistoryId.Resolution = value; }

        public DateTime StartDate { get => startDate; private set => startDate = value.ToUniversalTime(); }

        public DateTime EndDate { get => endDate; private set => endDate = value.ToUniversalTime(); }

        public string GetFilePath(string dataDir) => Path.Combine(dataDir, Key + ".bin3");

        public string Key => HistoryId.Key + $"_{StartDate:yyyyMMddHHmmss}_{EndDate:yyyyMMddHHmmss}";

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public HistoryChunkIdV3()
        {
            HistoryId = new SymbolHistoryId();
        }

        public HistoryChunkIdV3(SymbolHistoryId id, DateTime date, DateTime endDate)
        {
            this.HistoryId = id;
            this.StartDate = date;
            this.endDate = endDate;
        }

        public override int GetHashCode()
        {
            return this.Key.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            var other = obj as HistoryChunkIdV3;
            return other != null && this.Key == other.Key;
        }

        public static HistoryChunkIdV3 Parse(string filePath)
        {

            HistoryChunkIdV3 ret = null;
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var lastDivisor = fileName.IndexOf("_");
                string key = fileName.Substring(0, lastDivisor);
                string datestr = fileName.Substring(lastDivisor + 1);
                var symId = SymbolHistoryId.Parse(key);
                var startDate = DateTime.ParseExact(datestr, "yyyyMMddHHss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                var endDate = DateTime.ParseExact(datestr, "yyyyMMddHHss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                ret = new HistoryChunkIdV3(symId, startDate, endDate);
            }
            catch (Exception _ex)
            {
                Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }

            return ret;

        }
        public override string ToString()
        {
            return this.Key;
        }
    }

}
