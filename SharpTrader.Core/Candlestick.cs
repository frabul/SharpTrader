using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack;
using ProtoBuf;
namespace SharpTrader
{
    [ProtoContract]
    [MessagePackObject]
    public class Candlestick : ITradeBar
    {
        private DateTime openTime;
        private DateTime closeTime;
        [Key(0)]
        [ProtoMember(1)]
        public virtual DateTime OpenTime { get => openTime; set => openTime = value.Kind == DateTimeKind.Unspecified ? new DateTime(value.Ticks, DateTimeKind.Utc) : value; }
        [Key(1)]
        [ProtoMember(2)]
        public virtual DateTime CloseTime { get => closeTime; set => closeTime = value.Kind == DateTimeKind.Unspecified ? new DateTime(value.Ticks, DateTimeKind.Utc) : value; }
        [Key(2)]
        [ProtoMember(3)]
        public virtual double Open { get; set; }
        [Key(3)]
        [ProtoMember(4)]
        public virtual double High { get; set; }
        [Key(4)]
        [ProtoMember(5)]
        public virtual double Low { get; set; }
        [Key(5)]
        [ProtoMember(6)]
        public virtual double Close { get; set; }
        [Key(6)]
        [ProtoMember(7)]
        public virtual double QuoteAssetVolume { get; set; }

        [ProtoIgnore]
        [IgnoreMember]
        public TimeSpan Timeframe => CloseTime - OpenTime;

        [ProtoIgnore]
        [IgnoreMember]
        public DateTime Time => CloseTime;

        [ProtoIgnore]
        [IgnoreMember]
        public virtual double Length { get { return this.High - this.Low; } }
        [ProtoIgnore]
        [IgnoreMember]
        public double Value => this.Close;
        [ProtoIgnore]
        [IgnoreMember]
        public MarketDataKind Kind => MarketDataKind.TradeBar;

        public Candlestick() { }

        public Candlestick(DateTime openTime, ITradeBar toCopy, TimeSpan duration)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            QuoteAssetVolume = toCopy.QuoteAssetVolume;
            OpenTime = openTime;
            CloseTime = openTime + duration;
        }

        public Candlestick(ITradeBar toCopy)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            QuoteAssetVolume = toCopy.QuoteAssetVolume;
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
                QuoteAssetVolume = this.QuoteAssetVolume,
                OpenTime = this.OpenTime,
                CloseTime = this.CloseTime,
            };
        }

        public void Merge(ITradeBar c)
        {
            if (this.High < c.High)
                this.High = c.High;
            if (this.Low > c.Low)
                this.Low = c.Low;
            this.Close = c.Close;
            this.QuoteAssetVolume += c.QuoteAssetVolume;
        }
        public override bool Equals(object obj)
        {
            var c = obj as ITradeBar;
            if (c == null)
                return false;
            var equal =
                c.Close == this.Close &&
                c.CloseTime == this.CloseTime &&
                c.High == this.High &&
                c.Low == this.Low &&
                c.Open == this.Open &&
                c.OpenTime == this.OpenTime &&
                c.Time == this.Time &&
                c.Timeframe == this.Timeframe &&
                c.QuoteAssetVolume == this.QuoteAssetVolume;
            return equal;
        }
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
        public override string ToString()
        {
            return $"{{ {Time} Open:{Open:f7} Hi:{High:f7} Low:{Low:f7} Close:{Close:f7}  }}";
        }
    }
}
