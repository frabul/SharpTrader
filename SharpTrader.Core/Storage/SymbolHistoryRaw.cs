using System;
using System.Collections.Generic;
using ProtoBuf;

namespace SharpTrader.Storage
{
    [ProtoContract]
    class SymbolHistoryRaw
    {
        private List<Candlestick> _Ticks;
        public SymbolHistoryRaw()
        {
        }
        internal bool Equals(HistoryInfo info)
        {
            return info.market == Market && info.symbol == Symbol && info.Timeframe == Timeframe;
        }
        [ProtoIgnore]
        public readonly object Locker = new object();
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
}
