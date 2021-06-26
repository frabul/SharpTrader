using MessagePack;
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SharpTrader.Storage
{
    [Union(0, typeof(HistoryChunkIdV3))]
    [MessagePackObject]
    public abstract class HistoryChunkId
    {
        [Key(0)]
        public abstract SymbolHistoryId HistoryId { get; set; }
        [Key(1)]
        public abstract DateTime StartDate { get; set; }
        [Key(2)]
        public abstract DateTime EndDate { get; set; }
        public abstract string GetFileName();

        [IgnoreMember] public abstract string Key { get; }
        public static bool TryParse(string filePath, out HistoryChunkId retVal)
        {
            var extension = Path.GetExtension(filePath);
            retVal = null;
            if (extension == ".bin2")
                return HistoryChunkIdV2.TryParse(filePath, out retVal);
            else if (extension == ".bin3")
                return HistoryChunkIdV3.TryParse(filePath, out retVal);
            else if (extension == ".zip")
                return ChunkId_BinancePublic.TryParse(filePath, out retVal);
            else
                return false;
            //throw new InvalidOperationException($"Unknown file extension {extension}"); 
        }

        public override int GetHashCode()
        {
            return this.Key.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            var other = obj as HistoryChunkId;
            return other != null && this.Key == other.Key;
        }
        public override string ToString()
        {
            return this.Key;
        }

        public string GetFilePath(string dataDir)
        {
            return Path.Combine(dataDir, GetFileName());
        }

        public bool Overlaps(DateTime startDate, DateTime endDate)
        {
            return
                (this.StartDate >= startDate && this.StartDate < endDate) ||
                (this.EndDate > startDate && this.EndDate <= endDate) ||
                (startDate >= this.StartDate && startDate < this.EndDate);
        }
    }

    [ProtoContract]
    public class HistoryChunkIdV2 : HistoryChunkId
    {
        private DateTime startDate;
        private DateTime endDate;
        [ProtoMember(1)]
        public override SymbolHistoryId HistoryId { get; set; }

        [ProtoMember(2)]
        public override DateTime StartDate
        {
            get => startDate; set { startDate = value.ToUniversalTime(); endDate = startDate.AddMonths(1); }
        }

        public override DateTime EndDate { get => endDate; set { endDate = value; } }

        public override string GetFileName() => Key + ".bin2";

        public override string Key => HistoryId.Key + $"_{StartDate:yyyyMM}";

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

        public static new bool TryParse(string filePath, out HistoryChunkId retVal)
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
                retVal = new HistoryChunkIdV2()
                {
                    HistoryId = symId,
                    StartDate = date
                };
                return true;
            }
            catch (Exception _ex)
            {
                retVal = null;
                return false;
                //Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }
        }
        public override string ToString()
        {
            return this.Key;
        }
    }

    /// <summary>
    /// Data contained is > startDate and  <= endDate
    /// </summary>
    [MessagePackObject]
    public class HistoryChunkIdV3 : HistoryChunkId
    {

        private DateTime startDate;

        private DateTime endDate;

        [IgnoreMember]
        public override SymbolHistoryId HistoryId { get; set; }

        [IgnoreMember]
        public override DateTime StartDate { get => startDate; set => startDate = value.ToUniversalTime(); }

        [IgnoreMember]
        public override DateTime EndDate { get => endDate; set => endDate = value.ToUniversalTime(); }

        [IgnoreMember]
        public override string Key => HistoryId.Key + $"_{StartDate:yyyyMMddHHmmss}_{EndDate:yyyyMMddHHmmss}";

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public HistoryChunkIdV3()
        {

        }

        public HistoryChunkIdV3(SymbolHistoryId id, DateTime date, DateTime endDate)
        {
            this.HistoryId = id;
            this.StartDate = date;
            this.endDate = endDate;
        }

        public HistoryChunkIdV3(HistoryChunkId chunkId)
        {
            this.HistoryId = chunkId.HistoryId;
            this.StartDate = chunkId.StartDate;
            this.endDate = chunkId.EndDate;
        }

        static Regex FileNameRegex = new Regex(@"([A-Za-z0-9]+_[A-Z0-9]+_[0-9]+)_(\d+)_(\d+)[.]bin3", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.CultureInvariant);
        public static new bool TryParse(string filePath, out HistoryChunkId retVal)
        {
            retVal = null;
            try
            {
                var match = FileNameRegex.Match(filePath);
                if (match.Success)
                {
                    var symId = SymbolHistoryId.Parse(match.Groups[1].Value);
                    var startDate = DateTime.ParseExact(match.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
                    var endDate = DateTime.ParseExact(match.Groups[3].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
                    retVal = new HistoryChunkIdV3(symId, startDate, endDate);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception _ex)
            {
                return false;
                //Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }

        }

        public override string GetFileName() => Key + ".bin3";
    }

}
