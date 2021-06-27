
using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.WebSocket;
using BinanceExchange.API.Websockets;
using LiteDB;
using NLog;
using SharpTrader.BrokersApi.Binance;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace SharpTrader.BrokersApi.InteractiveBrokers
{
    class SymbolFeed : ISymbolFeed, IDisposable
    {
        public event Action<ISymbolFeed, IBaseData> OnData; 

        public DateTime Time { get; private set; }
        public double Ask { get; private set; }
        public double Bid { get; private set; }
        public string Market { get; private set; }
        public double Spread { get; set; }
        public double Volume24H { get; private set; }

        public SymbolInfo Symbol { get; }
        ISymbolInfo ISymbolFeed.Symbol => Symbol;

        MarketDataSubscription MarketData { get; set; }
        public SymbolFeed(SymbolInfo symbol, MarketDataSubscription sub)
        {
            Symbol = symbol;
            MarketData = sub;
        }

        private void HearthBeat(object state, ElapsedEventArgs args)
        {

        }

        public Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime)
        {
            return null;
        }

        public Task<TimeSerie<ITradeBar>> GetHistoryNavigator(TimeSpan resolution, DateTime historyStartTime)
        {
            return null;
        }

        public void Dispose()
        {

        }

        public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal amount, decimal price)
        {

            price = Utils.RoundNumberHigher(price, this.Symbol.PricePrecision);
            if (amount * price < Symbol.MinNotional)
                amount = Symbol.MinNotional / price;

            if (amount < Symbol.MinLotSize)
                amount = Symbol.MinLotSize;

            amount = Utils.RoundNumberHigher(amount, Symbol.LotSizeStep);


            return (price / 1.00000000000m, amount / 1.000000000000m);
        }

        public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal amount, decimal price)
        {

            price = Utils.RoundNumberLower(price, this.Symbol.PricePrecision);
            if (amount * price < Symbol.MinNotional)
                amount = 0;

            if (amount < Symbol.MinLotSize)
                amount = 0;

            amount = Utils.RoundNumberLower(amount, Symbol.LotSizeStep);

            return (price / 1.00000000000m, amount / 1.000000000000m);
        }

        Task<TimeSerie<ITradeBar>> ISymbolFeed.GetHistoryNavigator(DateTime historyStartTime)
        {
            throw new NotImplementedException();
        }

        (decimal price, decimal amount) ISymbolFeed.GetOrderAmountAndPriceRoundedDown(decimal oderAmout, decimal exitPrice)
        {
            throw new NotImplementedException();
        }

        (decimal price, decimal amount) ISymbolFeed.GetOrderAmountAndPriceRoundedUp(decimal oderAmout, decimal exitPrice)
        {
            throw new NotImplementedException();
        }
    }
}
