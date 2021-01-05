using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;



namespace SharpTrader.MarketSimulator
{
    public class SymbolFeed : ISymbolFeed
    {
        public event Action<ISymbolFeed, IBaseData> OnData;
        private TimeSpan Resolution = TimeSpan.FromSeconds(60);
        private List<IBaseData> NewData = new List<IBaseData>(10);

        ISymbolInfo ISymbolFeed.Symbol => Symbol;
        public SymbolInfo Symbol { get; private set; }
        public double Ask { get; private set; }
        public DateTime Time { get; internal set; }
        public double Bid { get; private set; }
        public string Market { get; private set; }
        public double Spread { get; set; }
        public ISymbolHistory DataSource { get; set; }
        public IBaseData LastTick { get; private set; }

        public SymbolFeed(string market, SymbolInfo symbol)
        {
            this.Symbol = symbol;
            this.Market = market;
        }

        public virtual Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime)
        {
            //todo fetch history from database
            TimeSerie<ITradeBar> newNavigator = new TimeSerie<ITradeBar>();
            //consolidate the currently available data
            using (var navigator = new TimeSerieNavigator<ITradeBar>(this.DataSource.Ticks))
            {
                //add all records up to current time
                navigator.SeekNearestBefore(historyStartTime);
                while (navigator.MoveNext() && navigator.Time <= this.DataSource.Ticks.Time)
                {
                    newNavigator.AddRecord(navigator.Current);
                }
            }
            return Task.FromResult(newNavigator);
        }

        internal void AddNewData(IBaseData newMarketData)
        {
            Bid = newMarketData.Value - Spread / 2;
            Ask = newMarketData.Value + Spread / 2;
            LastTick = newMarketData;
            NewData.Add(newMarketData);
        }

        public void RaisePendingEvents(ISymbolFeed sender)
        {
            if (NewData.Count > 0)
            {
                foreach (var data in NewData)
                {
                    OnData?.Invoke(this, data);
                }
                NewData.Clear();
            }
        }

        public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal amount, decimal price)
        {
            Debug.Assert(amount > 0);
            Debug.Assert(price > 0);
            amount = Math.Round(amount - 0.00049m, 4);
            if (amount * price < 0.00001m)
                amount = 0;
            return (price, amount);
        }

        public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal amount, decimal price)
        {
            Debug.Assert(amount > 0);
            Debug.Assert(price > 0);
            amount = Math.Round(amount + 0.00049m, 4);

            if (amount * price < 0.00001m)
                amount = 0.00001m / price;
            return (price, amount);
        }
    }
}
