using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    [Serializable]
    public class Candle
    {
        public double Open { get; set; }

        public double High { get; set; }

        public double Low { get; set; }

        public double Close { get; set; }

        public DateTime Time { get; set; }

        public DateTime CloseTime { get; set; }

        public TimeSpan Duration { get; set; }

        public double Volume { get; set; }
         
        public Candle Clone()
        {
            return new Candle()
            {
                Open = this.Open,
                Close = this.Close,
                High = this.High,
                Low = this.Low,
                Volume = this.Volume,
                Time = this.Time,
            };
        }

        public double Length { get { return this.High - this.Low; } }
    }
}
