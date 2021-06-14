using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MessagePack;
using NLog.LayoutRenderers;
using ProtoBuf;

namespace SharpTrader.Storage
{
    [ProtoContract]
    public class SymbolHistoryFile_Legacy
    {
        private List<Candlestick> _Ticks;
        public SymbolHistoryFile_Legacy()
        {
        }
        internal bool Equals(SymbolHistoryId info)
        {
            return info.Market == Market && info.Symbol == Symbol && info.Resolution == Timeframe;
        }
        [ProtoMember(6)]
        public virtual string FileName { get; set; }
        [ProtoMember(1)]
        public virtual string Market { get; set; }
        [ProtoMember(2)]
        public virtual string Symbol { get; set; }
        [ProtoMember(3)]
        public virtual TimeSpan Timeframe { get; set; }
        [ProtoMember(4)]
        public virtual List<Candlestick> Ticks
        {
            get { return _Ticks; }
            set
            {
                if (_Ticks != null)
                    throw new Exception("Modification not allowed.");
                else
                {
                    _Ticks = value;
                }
            }
        }
        [ProtoMember(5)]
        public virtual double Spread { get; set; }

    }
    [Union(0, typeof(HistoryChunkV3))]
    [MessagePackObject]
    public abstract class HistoryChunk
    {
        [Key(0)]
        public abstract HistoryChunkId ChunkId { get; set; }
        [Key(1)]
        public abstract List<Candlestick> Ticks { get; set; }
        public abstract Task SaveAsync(string filePath);
        public static async Task<HistoryChunk> Load(string filePath)
        {
            var fileExtension = Path.GetExtension(filePath);

            if (fileExtension == ".bin")
            {
                using (var fs = File.Open(filePath, FileMode.Open))
                {
                    SymbolHistoryFile_Legacy fdata = Serializer.Deserialize<SymbolHistoryFile_Legacy>(fs);
                    return new HistoryChunkV2(fdata);
                }
            }
            else if (fileExtension == ".bin2")
            {
                HistoryChunk fdata = HistoryChunkV2.LoadFrom(filePath);
                return fdata;
            }
            else if (fileExtension == ".bin3")
            {
                HistoryChunk fdata = await HistoryChunkV3.LoadFromAsync(filePath);
                return fdata;
            }
            else
            {
                throw new InvalidOperationException("Wrong extension for loading HistoryChunk.");
            }

        }


    }


    [ProtoContract]
    public class HistoryChunkV2 : HistoryChunk
    {
        private List<Candlestick> _Ticks;

        public override HistoryChunkId ChunkId { get => Id; set => Id = (HistoryChunkIdV2)value; }
        [ProtoMember(1)]
        public HistoryChunkIdV2 Id { get; set; }
        [ProtoMember(2)]
        public override List<Candlestick> Ticks
        {
            get { return _Ticks; }
            set
            {
                if (_Ticks != null)
                    throw new Exception("Modification not allowed.");
                else
                {
                    _Ticks = value;
                }
            }
        }

        public HistoryChunkV2()
        {
        }

        public HistoryChunkV2(SymbolHistoryFile_Legacy old)
        {
            this.Ticks = old.Ticks;
            ChunkId = new HistoryChunkIdV3(
                    new SymbolHistoryId(old.Market, old.Symbol, old.Timeframe),
                    old.Ticks.First().Time,
                    old.Ticks.First().Time.AddMonths(1)
                );
        }

        public override Task SaveAsync(string fileDir)
        {
            var filePath = Id.GetFilePath(fileDir);
            using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write))
            {
                Serializer.Serialize<HistoryChunkV2>(fileStream, this);
            }
            return Task.CompletedTask;
        }

        public static HistoryChunkV2 LoadFrom(string filePath)
        {
            using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                return Serializer.Deserialize<HistoryChunkV2>(fileStream);
            }
        }
    }

    [MessagePackObject]
    public class HistoryChunkV3 : HistoryChunk
    {

        private List<Candlestick> _Ticks;
        [IgnoreMember]
        public override HistoryChunkId ChunkId { get => Id; set => Id = (HistoryChunkIdV3)value; }
        [IgnoreMember]
        public HistoryChunkIdV3 Id { get; set; }
        [IgnoreMember]
        public override List<Candlestick> Ticks
        {
            get { return _Ticks; }
            set
            {
                if (_Ticks != null)
                    throw new Exception("Modification not allowed.");
                else
                {
                    _Ticks = value;
                }
            }
        }

        public HistoryChunkV3()
        {
        }

        public override async Task SaveAsync(string fileDir)
        {
            var filePath = Id.GetFilePath(fileDir);
            using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write))
            {
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray); 
                await MessagePackSerializer.SerializeAsync<HistoryChunkV3>(fileStream, this);

            }
        }

        public static async Task<HistoryChunkV3> LoadFromAsync(string filePath)
        {
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                return await MessagePackSerializer.DeserializeAsync<HistoryChunkV3>(fileStream);
            }
        }
    }
}
