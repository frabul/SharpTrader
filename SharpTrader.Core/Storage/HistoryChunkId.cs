﻿using MessagePack;
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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
        public static HistoryChunkId Parse(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            if (extension == ".bin2")
                return HistoryChunkIdV2.Parse(filePath);
            else if (extension == ".bin3")
                return HistoryChunkIdV3.Parse(filePath);
            else
                throw new InvalidOperationException($"Unknown file extension {extension}");
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
        public override string ToString()
        {
            return this.Key;
        }

        public string GetFilePath(string dataDir)
        {
            return Path.Combine(dataDir, GetFileName());
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

        public override DateTime EndDate { get => endDate; set { throw new InvalidOperationException("HistoryChunkIdV2 doesn't allow to set EndDate"); } }

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

        public static new HistoryChunkIdV2 Parse(string filePath)
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

        public static new HistoryChunkIdV3 Parse(string filePath)
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
        public override string GetFileName() => Key + ".bin3";
    }

}
