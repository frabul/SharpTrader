using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [ProtoContract]
    public class HistoryChunk
    {
        private List<Candlestick> _Ticks;
        [ProtoMember(1)]
        public HistoryChunkId ChunkId { get; set; }

        [ProtoMember(2)]
        public List<Candlestick> Ticks
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

        public HistoryChunk()
        {
        }

        public HistoryChunk(SymbolHistoryFile_Legacy old)
        {
            this.Ticks = old.Ticks;
            ChunkId = new HistoryChunkIdV3(
                    new SymbolHistoryId(old.Market, old.Symbol, old.Timeframe),
                    old.Ticks.First().Time,
                    old.Ticks.First().Time.AddMonths(1)
                );
        }

        public static HistoryChunk Load(string filePath)
        {
            var fileExtension = Path.GetExtension(filePath);
            using (var fs = File.Open(filePath, FileMode.Open))
            {
                if (fileExtension == ".bin")
                {
                    SymbolHistoryFile_Legacy fdata = Serializer.Deserialize<SymbolHistoryFile_Legacy>(fs);
                    return new HistoryChunk(fdata);
                }
                else if (fileExtension == ".bin2")
                {
                    HistoryChunk fdata = Serializer.Deserialize<HistoryChunk>(fs);
                    return fdata;
                }
                else if (fileExtension == ".bin3")
                {
                    using (FileStream fileStream = File.Open(filePath, FileMode.Open))
                    {
                        HistoryChunkId chunkId = BinaryPack.BinaryConverter.Deserialize<HistoryChunkIdV3>(fileStream);
                        List<Candlestick> candles = BinaryPack.BinaryConverter.Deserialize<List<Candlestick>>(fileStream);
                        return new HistoryChunk() { ChunkId = chunkId, Ticks = candles };
                    }
                }
                else
                {
                    throw new InvalidOperationException("Wrong extension for loading HistoryChunk.");
                }
            }
        }

        public void Save(string filePath)
        {
            using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write))
            {
                //BinaryPack.BinaryConverter.Serialize<HistoryChunkIdV3>(this.ChunkId as HistoryChunkIdV3, fileStream);

                //var asd = BinaryPack.BinaryConverter.Serialize(this.Ticks );
                BinaryPack.BinaryConverter.Serialize(this, fileStream);
            }
        }
    }
    [ProtoContract]
    public class HistoryChunkV3
    {
        private List<Candlestick> _Ticks;
        [ProtoMember(1)]
        public HistoryChunkIdV3 ChunkId { get; set; }

        [ProtoMember(2)]
        public List<Candlestick> Ticks
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



        public static HistoryChunkV3 Load(string filePath)
        {
            var fileExtension = Path.GetExtension(filePath);
            using (var fs = File.Open(filePath, FileMode.Open))
            {
                if (fileExtension == ".bin")
                {
                    throw new NotImplementedException();
                }
                else if (fileExtension == ".bin2")
                {
                    throw new NotImplementedException();
                }
                else if (fileExtension == ".bpack")
                {

                    //HistoryChunkIdV3 chunkId = BinaryPack.BinaryConverter.Deserialize<HistoryChunkIdV3>(fs);
                    //List<Candlestick> candles = BinaryPack.BinaryConverter.Deserialize<List<Candlestick>>(fs);

                    //return new HistoryChunkV3() { ChunkId = chunkId, Ticks = candles };
                    return BinaryPack.BinaryConverter.Deserialize<HistoryChunkV3>(fs);
                }
                else
                {
                    throw new InvalidOperationException("Wrong extension for loading HistoryChunk.");
                }
            }
        }

        public void Save(string filePath)
        {
            using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write))
            {
                //BinaryPack.BinaryConverter.Serialize<HistoryChunkIdV3>(this.ChunkId as HistoryChunkIdV3, fileStream);

                //var asd = BinaryPack.BinaryConverter.Serialize(this.Ticks );
                BinaryPack.BinaryConverter.Serialize(this, fileStream);
                fileStream.Close();
            }
        }
    }
}
