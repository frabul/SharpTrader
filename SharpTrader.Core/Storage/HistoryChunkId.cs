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
        [ProtoMember(1)]
        public SymbolHistoryId HistoryId { get; private set; }

        [ProtoMember(2)]
        public DateTime StartDate { get; private set; }

        [ProtoMember(3)]
        public string FilePath { get; private set; }

        public string Key => HistoryId.Key + $"_{StartDate:yyyyMM}";

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public HistoryChunkId()
        {

        }

        public HistoryChunkId(string dataDir, SymbolHistoryId id, DateTime date)
        { 
            HistoryId = id;
            this.StartDate = date;
            FilePath = Path.Combine(dataDir, this.Key + ".bin2");
        }

        public override int GetHashCode()
        {
            return this.Key.GetHashCode();
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
                var date = DateTime.ParseExact(datestr, "yyyyMM", CultureInfo.InvariantCulture);
                ret = new HistoryChunkId()
                {
                    FilePath = filePath,
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
    }

}
