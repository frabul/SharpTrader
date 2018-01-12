using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZeroFormatter;
using ProtoBuf;
namespace SharpTrader
{
    [ZeroFormattable, ProtoContract]
    public class Candlestick : ICandlestick
    {
        [Index(0), ProtoMember(1)]
        public virtual DateTime OpenTime { get; set; }

        [Index(1), ProtoMember(2)]
        public virtual DateTime CloseTime { get; set; }

        [Index(2), ProtoMember(3)]
        public virtual double Open { get; set; }

        [Index(3), ProtoMember(4)]
        public virtual double High { get; set; }

        [Index(4), ProtoMember(5)]
        public virtual double Low { get; set; }

        [Index(5), ProtoMember(6)]
        public virtual double Close { get; set; }

        [Index(6), ProtoMember(7)]
        public virtual double Volume { get; set; }

        [IgnoreFormat, ProtoIgnore]
        public TimeSpan Timeframe => CloseTime - OpenTime;

        [IgnoreFormat, ProtoIgnore]
        public DateTime Time => CloseTime;

        [IgnoreFormat, ProtoIgnore]
        public virtual double Length { get { return this.High - this.Low; } }

        public Candlestick() { }

        public Candlestick(ICandlestick toCopy, TimeSpan duration)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            Volume = toCopy.Volume;
            OpenTime = toCopy.OpenTime;
            CloseTime = toCopy.CloseTime + duration;
        }

        public Candlestick(ICandlestick toCopy)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            Volume = toCopy.Volume;
            OpenTime = toCopy.OpenTime;
            CloseTime = toCopy.CloseTime;
        }

        public Candlestick Clone()
        {
            return new Candlestick()
            {
                Open = this.Open,
                Close = this.Close,
                High = this.High,
                Low = this.Low,
                Volume = this.Volume,
                OpenTime = this.OpenTime,
                CloseTime = this.CloseTime,
            };
        }

        internal void Merge(ICandlestick c)
        {
            throw new NotImplementedException();
        }
    }
}
