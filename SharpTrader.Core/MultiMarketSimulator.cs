using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        Market[] _Markets;
        Dictionary<string, ISymbolHistory> SymbolsData;
        HistoricalRateDataBase Histories;


        public DateTime Time { get; private set; }

        public MultiMarketSimulator(string dataDirectory, HistoricalRateDataBase historyDb)
        {
            Histories = historyDb;
        }

        public IEnumerable<IMarketApi> Markets => _Markets;

        public IEnumerable<string> GetSymbols(string market)
        {
            throw new NotImplementedException();
        }



        public void Run()
        {
            //find the nearest candle of all SymbolsData between the symbols that have been requested
            DateTime nextTick = DateTime.MaxValue;
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var sdata = SymbolsData[feed.Market + "_" + feed.Symbol];
                    if (nextTick > sdata.Ticks.NextTickTime)
                        nextTick = sdata.Ticks.NextTickTime;
                }

            if (nextTick == DateTime.MaxValue)
                return;

            //update market time
            this.Time = nextTick;

            //add new candle to all symbol feeds that have it
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var data = SymbolsData[market.Name + "_" + feed.Symbol];
                    if (data.Ticks.NextTickTime <= this.Time)
                    {
                        data.Ticks.Next();
                        market.AddNewCandle(feed, data.Ticks.Tick);
                    }
                }
            //raise orders/trades events


            //raise symbol feeds events
        }



        public class MarketInfo
        {
            string Name;
            double Fee;

        }

        
    }


}
