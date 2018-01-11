using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        private static string ConfigFile = "MarketsSimulator.json";
        private Market[] _Markets;
        private Dictionary<string, ISymbolHistory> SymbolsData;
        private HistoricalRateDataBase HistoryDb;
        private Configuration Config;

        public IEnumerable<IMarketApi> Markets => _Markets;
        public DateTime Time { get; private set; }


        public MultiMarketSimulator(string dataDirectory, HistoricalRateDataBase historyDb)
        {
            HistoryDb = historyDb;
            var text = File.ReadAllText(dataDirectory + ConfigFile);
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(text);

            this._Markets = new Market[Config.Markets.Length];
            int i = 0;
            foreach (var mc in Config.Markets)
            {
                var market = new Market(mc.MarketName, mc.MakerFee, mc.TakerFee);
                _Markets[i++] = market;
            }
        }


        public IEnumerable<string> GetSymbols(string market)
        {
            throw new NotImplementedException();
        }

        public IMarketApi GetMarketApi(string marketName)
        {
            var market = Markets.Where(m => m.MarketName == marketName).FirstOrDefault();
            if (market == null)
                throw new Exception($"Market {marketName} not found.");
            return market;
        }

        public void Run()
        {
            //find the nearest candle of all SymbolsData between the symbols that have been requested
            DateTime nextTick = DateTime.MaxValue;
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var sdata = SymbolsData[feed.Market + "_" + feed.Symbol];
                    if (nextTick > sdata.Ticks.NextTick.CloseTime)
                        nextTick = sdata.Ticks.NextTick.CloseTime;
                }

            if (nextTick == DateTime.MaxValue)
                return;

            //update market time
            this.Time = nextTick;

            //add new candle to all symbol feeds that have it
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var data = SymbolsData[market.MarketName + "_" + feed.Symbol];
                    while (data.Ticks.NextTickTime <= this.Time)
                    {
                        data.Ticks.Next();
                        market.AddNewCandle(feed as SymbolFeed, new Candlestick(data.Ticks.Tick));
                    }
                }

            //raise orders/trades events
            foreach (var market in _Markets)
                market.RaisePendingEvents();

            //raise symbol feeds events
        }


        class Configuration
        {
            public MarketConfiguration[] Markets { get; set; }
            public SymbolConfiguration[] Symbols { get; set; }
        }

        public class MarketInfo
        {
            string Name;
            double Fee;

        }


    }


}
