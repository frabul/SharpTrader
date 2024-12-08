
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{

    public class TestTradeBarConsolidator
    {

        public void SimpleTest()
        {
            Random rand = new Random(1);
            TradeBarConsolidator consolidator = new TradeBarConsolidator(TimeSpan.FromHours(1));
            var timeNow = new DateTime(2022, 01, 01, 11, 11, 0, 0, DateTimeKind.Utc);
            consolidator.OnConsolidated += (obj) => Console.WriteLine($"Emitted {obj} at {timeNow:yyyy-MM-dd HH-mm}"); ;

            Candlestick GetCandle(DateTime time) =>
                new Candlestick()
                {
                    OpenTime = time.AddMinutes(-1),
                    CloseTime = time,
                    Open = rand.Next(1000),
                    High = rand.Next(1000),
                    Low = rand.Next(1000),
                    Close = rand.Next(1000)
                };

            while (timeNow < new DateTime(2022, 01, 02))
            {
                if(timeNow.AddMinutes(1) >= new DateTime(2022, 01, 02))
                { 
                }
                timeNow = timeNow.AddMinutes(1);
                consolidator.Update(GetCandle(timeNow));
            }

            timeNow = timeNow.AddHours(7.2);

            while (timeNow < new DateTime(2022, 01, 03))
            {
                timeNow = timeNow.AddMinutes(1); 
                consolidator.Update(GetCandle(timeNow)); 
            }
        } 
    }
}
