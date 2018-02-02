using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator : IMarketsManager
    {
        private static string ConfigFile = "MarketsSimulator.json";
        private Market[] _Markets;
        private Dictionary<string, ISymbolHistory> SymbolsData = new Dictionary<string, ISymbolHistory>();
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
                var market = new Market(mc.MarketName, mc.MakerFee, mc.TakerFee, dataDirectory, 1);
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

        public void Run(DateTime startTime, DateTime endTime, DateTime historyStart)
        {
            bool raiseEvents = false;
            while (NextTick(raiseEvents) && this.Time < endTime)
            {
                raiseEvents = startTime <= this.Time;
            }
        }

        public bool NextTick(bool raiseEvents)
        {
            //find the nearest candle of all SymbolsData between the symbols that have been requested
            DateTime nextTick = DateTime.MaxValue;
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var key = market.MarketName + "_" + feed.Symbol;
                    SymbolsData.TryGetValue(key, out var sdata);
                    if (sdata == null)
                    {
                        sdata = this.HistoryDb.GetSymbolHistory(market.MarketName, feed.Symbol, TimeSpan.FromSeconds(60));
                        SymbolsData.Add(key, sdata);
                    }
                    if (!sdata.Ticks.EndOfSerie && nextTick > sdata.Ticks.NextTick.CloseTime)
                        nextTick = sdata.Ticks.NextTick.CloseTime;
                }

            if (nextTick == DateTime.MaxValue)
                return false;

            //update market time
            this.Time = nextTick;

            //add new candle to all symbol feeds that have it
            foreach (var market in _Markets)
                foreach (var feed in market.Feeds)
                {
                    var data = SymbolsData[market.MarketName + "_" + feed.Symbol];
                    if (data.Ticks.Count > 0)
                        while (data.Ticks.NextTickTime <= this.Time)
                        {
                            data.Ticks.Next();
                            market.AddNewCandle(feed as SymbolFeed, new Candlestick(data.Ticks.Tick));
                        }
                }
            foreach (var market in _Markets)
                market.ResolveOrders();

            if (raiseEvents)
            {
                //raise orders/trades events
                foreach (var market in _Markets)
                    market.RaisePendingEvents();
            }
            return true;
        }

        class Configuration
        {
            public MarketConfiguration[] Markets { get; set; }
            public SymbolConfiguration[] Symbols { get; set; }
        }
    }


}
