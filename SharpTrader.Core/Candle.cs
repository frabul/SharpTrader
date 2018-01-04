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
        public float Open { get; set; }
 
        public float High { get; set; }
      
        public float Low { get; set; }
    
        public float Close { get; set; }
        
        public DateTime Time { get; set; }

        public TimeSpan Duration { get; set; }

        public int Volume { get; set; }
         
      

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
