using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SharpTrader.Storage
{
    [ProtoContract]
    public class HistoryChunkId
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
        public HistoryChunkId()
        {

        }

        public HistoryChunkId(SymbolHistoryId id, DateTime date)
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
            var other = obj as HistoryChunkId;
            return other!= null && this.Key == other.Key;
        }

        public static HistoryChunkId Parse(string filePath)
        {
            HistoryChunkId ret = null;
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var lastDivisor = fileName.LastIndexOf("_");
                string key = fileName.Substring(0, lastDivisor);
                string datestr = fileName.Substring(lastDivisor + 1);
                var symId = SymbolHistoryId.Parse(key);
                var date = DateTime.ParseExact(datestr, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                ret = new HistoryChunkId()
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

}
