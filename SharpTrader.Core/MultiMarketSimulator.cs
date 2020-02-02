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

        private HistoricalRateDataBase HistoryDb;
        private Configuration Config;
        private DateTime FirstTickTime = DateTime.MaxValue;
        private TimeSpan Resolution = TimeSpan.Zero;
        private DateTime _startOfSimulation = new DateTime(2000, 1, 1);

        public IEnumerable<IMarketApi> Markets => _Markets;
        public DateTime Time { get; private set; }

        private DateTime NextTickTime;

        public DateTime StartOfHistoryData
        {
            get => _startOfSimulation; set
            {
                _startOfSimulation = value;
                if (FirstTickTime == DateTime.MaxValue)
                    this.Time = StartOfHistoryData;
                foreach (var market in _Markets)
                    market.Time = this.Time;
            }
        }

        public MultiMarketSimulator(string dataDirectory, Configuration config, HistoricalRateDataBase historyDb)
        {
            Config = config;
            HistoryDb = historyDb;
            this._Markets = new Market[Config.Markets.Length];
            int i = 0;
            foreach (var mc in Config.Markets)
            {
                var market = new Market(mc.MarketName, mc.MakerFee, mc.TakerFee, dataDirectory);
                _Markets[i++] = market;
            }
        }

        public MultiMarketSimulator(string dataDirectory, HistoricalRateDataBase historyDb)
        {
            HistoryDb = historyDb;
            var text = File.ReadAllText(dataDirectory + ConfigFile);
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(text);

            this._Markets = new Market[Config.Markets.Length];
            int i = 0;
            foreach (var mc in Config.Markets)
            {
                var market = new Market(mc.MarketName, mc.MakerFee, mc.TakerFee, dataDirectory);
                _Markets[i++] = market;
            }
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
            //if we don't have yet a first tick then we need to try and load the first tick for all symbols
            if (FirstTickTime == DateTime.MaxValue)
            {
                //find the nearest candle of all SymbolsData between the symbols that have been requested 
                foreach (var market in _Markets)
                    foreach (var feed in market.SymbolsFeeds.Values)
                    {
                        if (feed.DataSource == null)
                        {
                            var histInfo = new HistoryInfo(market.MarketName, feed.Symbol.Key, TimeSpan.FromSeconds(60));
                            feed.DataSource = this.HistoryDb.GetSymbolHistory(histInfo, StartOfHistoryData);
                            this.HistoryDb.CloseFile(histInfo);
                        }
                        if (feed.DataSource.Ticks.Count > 0 && FirstTickTime > feed.DataSource.Ticks.StartTime) 
                            FirstTickTime = feed.DataSource.Ticks.StartTime;  
                    }
            }
            
            if (FirstTickTime == DateTime.MaxValue)
                throw new Exception("Error...FirstTickTime not found");

            Resolution = TimeSpan.FromSeconds(60);
            var nextTick = FirstTickTime + Resolution;

            //update market time
            this.Time = nextTick;

            //add new data to all symbol feeds that have it  
            //todo it's possible to optimize this loop by remembering the minimum next tick time of all sources
            this.NextTickTime = default(DateTime);
            foreach (var market in _Markets)
                foreach (var feed in market.SymbolsFeeds.Values)
                {
                    var dataSource = feed.DataSource;
                    if (dataSource.Ticks.Count > 0)
                        while (dataSource.Ticks.NextTickTime <= this.Time)
                        {
                            dataSource.Ticks.MoveNext();
                            var candle = dataSource.Ticks.Current is Candlestick c ? c : new Candlestick(dataSource.Ticks.Current);
                            market.AddNewCandle(feed as SymbolFeed, candle);// new Candlestick(data.Ticks.Tick)); //use less memory
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

        public void Deposit(string market, string asset, decimal amount)
        {
            _Markets.Where(m => m.MarketName == market).First().AddBalance(asset, amount);
        }


        public class Configuration
        {
            public MarketConfiguration[] Markets { get; set; }
        }
    }


}
