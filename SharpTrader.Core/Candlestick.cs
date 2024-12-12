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
        public DateTime OpenTime { get => openTime; set => openTime = value.Kind == DateTimeKind.Unspecified ? new DateTime(value.Ticks, DateTimeKind.Utc) : value; }
        [Key(1)]
        [ProtoMember(2)]
        public DateTime CloseTime { get => closeTime; set => closeTime = value.Kind == DateTimeKind.Unspecified ? new DateTime(value.Ticks, DateTimeKind.Utc) : value; }
        [Key(2)]
        [ProtoMember(3)]
        public double Open { get; set; }
        [Key(3)]
        [ProtoMember(4)]
        public double High { get; set; }
        [Key(4)]
        [ProtoMember(5)]
        public double Low { get; set; }
        [Key(5)]
        [ProtoMember(6)]
        public double Close { get; set; }
        [Key(6)]
        [ProtoMember(7)]
        public double QuoteAssetVolume { get; set; }

        [ProtoIgnore]
        [IgnoreMember]
        public TimeSpan Timeframe => CloseTime - OpenTime;

        [ProtoIgnore]
        [IgnoreMember]
        public DateTime Time => CloseTime;

        [ProtoIgnore]
        [IgnoreMember]
        public double Length { get { return this.High - this.Low; } }
        [ProtoIgnore]
        [IgnoreMember]
        public double Value => this.Close;
        [ProtoIgnore]
        [IgnoreMember]
        public MarketDataKind Kind => MarketDataKind.TradeBar;
        [ProtoIgnore]
        [IgnoreMember]
        public static Candlestick Default { get; } = new Candlestick();
        public bool IsDefault()
        {
            return CloseTime == default(DateTime);
        }

        public Candlestick() { }

        public Candlestick(DateTime ot, DateTime ct, double o, double h, double l, double c,  double vol)
        {
            Open = o;
            Close = c;
            High = h;
            Low = l;
            QuoteAssetVolume = vol;
            openTime = ot;
            closeTime = ct;
        }

        public void SetData(DateTime openTime, TimeSpan duration, ITradeBar toCopy)
        {
            this.OpenTime = openTime;
            this.CloseTime = openTime + duration;

            this.Open = toCopy.Open;
            this.High = toCopy.High;
            this.Low = toCopy.Low;
            this.Close = toCopy.Close; 
            this.QuoteAssetVolume = toCopy.QuoteAssetVolume;

        }

        public Candlestick(ITradeBar toCopy)
        {
            Open = toCopy.Open;
            Close = toCopy.Close;
            High = toCopy.High;
            Low = toCopy.Low;
            QuoteAssetVolume = toCopy.QuoteAssetVolume;
            openTime = toCopy.OpenTime;
            closeTime = toCopy.CloseTime;
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
                c.CloseTime == this.CloseTime &&
                c.OpenTime == this.OpenTime && 
                c.Open == this.Open &&
                c.High == this.High &&
                c.Low == this.Low &&
                c.Close == this.Close && 
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

        public static Candlestick GetFiller(Candlestick precedingCandle)
        {
            return new Candlestick(
               precedingCandle.CloseTime,
               precedingCandle.CloseTime + precedingCandle.Timeframe,
               precedingCandle.Close,
               precedingCandle.Close,
               precedingCandle.Close,
               precedingCandle.Close,
               0
           );
        }
    }
}
