﻿using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.MarketSimulator
{
    public partial class MultiMarketSimulator : IMarketsManager
    {
        private static string ConfigFile = "MarketsSimulator.json";
        private MarketEmulator[] _Markets;

        private TradeBarsRepository HistoryDb;
        private Configuration Config;
        private TimeSpan Resolution = TimeSpan.FromSeconds(60);
        public IEnumerable<IMarketApi> Markets => _Markets;
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public DateTime Time { get; private set; }

        public MultiMarketSimulator(string dataDirectory, Configuration config, TradeBarsRepository historyDb, DateTime simulationStartTime, DateTime endTime)
        {
            this.Time = simulationStartTime;
            StartTime = simulationStartTime;
            EndTime = endTime;

            Config = config;
            HistoryDb = historyDb;
            this._Markets = new MarketEmulator[Config.Markets.Length];
            int i = 0;
            foreach (var mc in Config.Markets)
            {
                var market = new MarketEmulator(mc.MarketName, mc.MakerFee, mc.TakerFee, dataDirectory, InitializeDataSourceCallBack);
                market.AllowBorrow = mc.AllowBorrow;
                _Markets[i++] = market;
            }
        }

        public MultiMarketSimulator(string dataDirectory, TradeBarsRepository historyDb, DateTime simulationStartTime, DateTime endTime)
        {
            //let's make the start and end times a multiple of Resolution timespan
            //var startTimeTicks = simulationStartTime.Ticks - simulationStartTime.Ticks % Resolution.Ticks;
            //var endTimeTicks = endTime.Ticks - endTime.Ticks % Resolution.Ticks; 
            this.Time = simulationStartTime;
            StartTime = simulationStartTime;
            EndTime = endTime;

            HistoryDb = historyDb;
            var text = File.ReadAllText(Path.Combine(dataDirectory, ConfigFile));
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(text);

            this._Markets = new MarketEmulator[Config.Markets.Length];
            int i = 0;
            foreach (var mc in Config.Markets)
            {
                var market = new MarketEmulator(mc.MarketName, mc.MakerFee, mc.TakerFee, dataDirectory, InitializeDataSourceCallBack);
                market.AllowBorrow = mc.AllowBorrow;
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
            while (NextTick() && this.Time < endTime)
            {

            }
        }

        int NoMoreDataCount = 0;
        public bool IncrementalHistoryLoading { get; set; } = false;
        public bool NextTick()
        {
            //calculate next tick time
            var nextTick = this.Time + Resolution;

            //if a new month has started


            //update market time
            this.Time = nextTick;
            bool moreData = false;
            //add new data to all symbol feeds that have it    
            foreach (var market in _Markets)
            {
                if (IncrementalHistoryLoading)  
                    if (Time.Month != market.Time.Month || Time.Year != market.Time.Year || !market.FistTickPassed)
                    {
                        
                        // a new month has started, let's load the data for this month
                        foreach (var feed in market.SymbolsFeeds.Values)
                        {
                            var histInfo = new SymbolHistoryId(market.MarketName, feed.Symbol.Key, TimeSpan.FromSeconds(60));

                            feed.DataSource = this.HistoryDb.GetSymbolHistory(
                                histInfo,
                                this.Time,
                                new DateTime(Time.Year, Time.Month, 1, 0, 0, 0).AddMonths(1).AddMilliseconds(-1));
                            this.HistoryDb.SaveAndClose(histInfo, false);
                            market.FistTickPassed = true;
                        }
                  
                    }
                market.Time = this.Time;
                foreach (var feed in market.SymbolsFeeds.Values)
                {
                    feed.Time = market.Time;
                    var dataSource = feed.DataSource;
                    if (dataSource.Ticks.Count > 0)
                    {
                        while (dataSource.Ticks.NextTickTime <= this.Time)
                        {
                            dataSource.Ticks.MoveNext();
                            var candle = dataSource.Ticks.Current is Candlestick c ? c : new Candlestick(dataSource.Ticks.Current);
                            market.AddNewCandle(feed as SymbolFeed, candle);// new Candlestick(data.Ticks.Tick)); //use less memory   
                        }
                        moreData |= dataSource.Ticks.Position < dataSource.Ticks.Count - 1;
                    }
                }
            }

            if (!moreData)
                NoMoreDataCount++;
            else
                NoMoreDataCount = 0;
            //resolve orders of each market
            foreach (var market in _Markets)
                market.ResolveOrders();

            //raise orders/trades events
            foreach (var market in _Markets)
                market.RaisePendingEvents();

            return nextTick <= this.EndTime && NoMoreDataCount < 10;
        }

        private void InitializeDataSourceCallBack(MarketEmulator market, SymbolFeed feed)
        {
            if (!IncrementalHistoryLoading)
                if (feed.DataSource == null)
                {
                    var histInfo = new SymbolHistoryId(market.MarketName, feed.Symbol.Key, TimeSpan.FromSeconds(60));
                    feed.DataSource = this.HistoryDb.GetSymbolHistory(histInfo, StartTime, EndTime);
                    //this.HistoryDb.CloseFile(histInfo);
                }
        }

        public void Deposit(string market, string asset, decimal amount)
        {
            _Markets.Where(m => m.MarketName == market).First().AddBalance(asset, amount);
        }

        public class Configuration
        {
            public MarketConfiguration[] Markets { get; set; }
        }

        public IEnumerable<ITrade> Trades => Markets.SelectMany(m => m.Trades);

        public decimal GetEquity(string baseAsset)
        {
            return Markets.Sum(m => m.GetEquity(baseAsset).Result.Result);
        }
    }


}
