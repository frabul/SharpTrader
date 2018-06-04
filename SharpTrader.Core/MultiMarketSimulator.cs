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
        private DateTime FirstTickTime = DateTime.MaxValue;
        private TimeSpan Delta = TimeSpan.Zero;
        private DateTime _startOfSimulation = new DateTime(2000, 1, 1);

        public IEnumerable<IMarketApi> Markets => _Markets;
        public DateTime Time { get; private set; }
        public DateTime StartOfSimulation
        {
            get => _startOfSimulation; set
            {
                _startOfSimulation = value;
                if (FirstTickTime == DateTime.MaxValue)
                    this.Time = StartOfSimulation;
                foreach (var market in _Markets)
                    market.Time = this.Time;
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
            if (FirstTickTime == DateTime.MaxValue)
            {
                //find the nearest candle of all SymbolsData between the symbols that have been requested

                foreach (var market in _Markets)
                    foreach (var feed in market.Feeds)
                    {
                        var key = market.MarketName + "_" + feed.Symbol;
                        SymbolsData.TryGetValue(key, out var sdata);
                        if (sdata == null)
                        {
                            sdata = this.HistoryDb.GetSymbolHistory(market.MarketName, feed.Symbol, TimeSpan.FromSeconds(60), StartOfSimulation);
                            SymbolsData.Add(key, sdata);
                        }
                        if (sdata.Ticks.Count > 0)
                            FirstTickTime = FirstTickTime > sdata.Ticks.FirstTickTime ? sdata.Ticks.FirstTickTime : FirstTickTime;
                    }
            }
            if (FirstTickTime == DateTime.MaxValue)
                throw new Exception("Error...FirstTickTime not found");
            Delta = Delta + TimeSpan.FromSeconds(60);
            var nextTick = FirstTickTime + Delta;


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
                            var candle = data.Ticks.Tick is Candlestick c ? c : new Candlestick(data.Ticks.Tick);
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


        class Configuration
        {
            public MarketConfiguration[] Markets { get; set; }
            public SymbolConfiguration[] Symbols { get; set; }
        }
    }


}
