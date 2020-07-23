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
            ChunkId = new HistoryChunkId(
                    old.FileName,
                    new SymbolHistoryId(old.Market, old.Symbol, old.Timeframe),
                    old.Ticks.First().Time
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
                else
                {
                    throw new InvalidOperationException("Wrong extension for loading HistoryChunk.");
                }
            }
        }
    }
}
