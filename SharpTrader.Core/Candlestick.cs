using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProtoBuf;
namespace SharpTrader
{
    [ProtoContract]
    public class Candlestick : ICandlestick
    {
        [ProtoMember(1)]
        public virtual DateTime OpenTime { get; set; }

        [ProtoMember(2)]
        public virtual DateTime CloseTime { get; set; }

        [ProtoMember(3)]
        public virtual double Open { get; set; }

        [ProtoMember(4)]
        public virtual double High { get; set; }

        [ProtoMember(5)]
        public virtual double Low { get; set; }

        [ProtoMember(6)]
        public virtual double Close { get; set; }

        [ProtoMember(7)]
        public virtual double Volume { get; set; }

        [ProtoIgnore]
        public TimeSpan Timeframe => CloseTime - OpenTime;

        [ProtoIgnore]
        public DateTime Time => CloseTime;

        [ProtoIgnore]
        public virtual double Length { get { return this.High - this.Low; } }

        public Candlestick() { }

        public Candlestick(DateTime openTime, ICandlestick toCopy, TimeSpan duration)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            Volume = toCopy.Volume;
            OpenTime = openTime;
            CloseTime = openTime + duration;
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

        public void Merge(ICandlestick c)
        {
            if (this.High < c.High)
                this.High = c.High;
            if (this.Low > c.Low)
                this.Low = c.Low;
            this.Close = c.Close;
            this.Volume += c.Volume;
        }
    }
}
