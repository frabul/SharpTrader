using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        class Market : IMarketApi
        {
            object locker = new object();
            private Dictionary<string, double> Balances = new Dictionary<string, double>();
            private bool OnTradePending = true;
            private List<Trade> Trades = new List<Trade>();
            private Dictionary<string, SymbolFeed> SymbolsFeed = new Dictionary<string, SymbolFeed>();
            private List<Order> PendingOrders = new List<Order>();
            private List<Order> ClosedOrders = new List<Order>();

            public string Name { get; private set; }
            public double MakerFee { get; private set; } = 0.0015;
            public double TakerFee { get; private set; } = 0.0025;
            public DateTime Time { get; internal set; }

            public IEnumerable<SymbolFeed> ActiveFeeds { get { lock (locker) return SymbolsFeed.Values; } }

            public Market(string name, double makerFee, double takerFee)
            {
                Name = name;
                MakerFee = makerFee;
                TakerFee = takerFee;
            }

            public ISymbolFeed GetSymbolFeed(string symbol)
            {
                SymbolFeed sf;
                var feedFound = SymbolsFeed.TryGetValue(symbol, out sf);
                if (!feedFound)
                { 
                    //TODO request feed creation

                    //var symbolData = SymbolsData.Where(sd => sd.Market == market && sd.Symbol == symbol).FirstOrDefault();
                    //if (symbolData == null)
                    //    throw new Exception($"Symbol {symbol} data not found for market {market}");
                    ////todo create symbol feed, add the data up to current date and 
                    string asset;
                    string counterAsset;
                    Utils.GetAssets(symbol, out asset, out counterAsset);
                    sf = new SymbolFeed(this.Name, asset, counterAsset);
                    lock (locker)
                        SymbolsFeed.Add(symbol, sf);
                }
                return sf;
            }

            public IMarketOperation LimitOrder(string symbol, TradeType type, double amount, double rate)
            {

                var order = new Order(this.Name, symbol, type, OrderType.Limit, amount, rate);
                lock (locker)
                    this.PendingOrders.Add(order);
                return new MarketOperation() { Status = MarketOperationStatus.Completed };

            }

            public IMarketOperation MarketOrder(string symbol, TradeType type, double amount)
            {
                lock (locker)
                {
                    var feed = SymbolsFeed[symbol];
                    var rate = type == TradeType.Buy ? feed.Ask : feed.Bid;
                    var order = new Order(this.Name, symbol, type, OrderType.Market, amount, rate);

                    var trade = new Trade(this.Name, symbol, this.Time, type, rate, amount, this.TakerFee * amount * rate);
                    RegisterTrade(feed, trade);
                    this.ClosedOrders.Add(order);
                }

                return new MarketOperation() { Status = MarketOperationStatus.Completed };
            }

            internal void RaisePendingEvents()
            {
                foreach (var feed in SymbolsFeed.Values)
                    feed.RaisePendingEvents();
            }

            internal void ResolveOrders()
            {
                //resolve orders/trades 
                lock (locker)
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        var feed = SymbolsFeed[order.Symbol];
                        if (order.Type == OrderType.Limit)
                        {
                            var willBuy = (order.TradeType == TradeType.Buy && feed.Ticks.Tick.Low + feed.Spread <= order.Rate);
                            var willSell = (order.TradeType == TradeType.Sell && feed.Ticks.Tick.High >= order.Rate);

                            if (willBuy || willSell)
                            {
                                var trade = new Trade(
                                    market: this.Name,
                                    symbol: feed.Symbol,
                                    time: feed.Ticks.Tick.Time,
                                    price: order.Rate,
                                    amount: order.Amount,
                                    type: order.TradeType,
                                    fee: this.MakerFee * order.Amount * order.Rate
                                );
                                RegisterTrade(feed, trade);
                                order.Status = OrderStatus.Filled;
                            }
                        }
                    }
            }

            internal void AddNewCandle(SymbolFeed feed, Candle tick)
            {
                Time = tick.CloseTime;
                feed.AddNewCandle(tick);
            }

            private void RegisterTrade(SymbolFeed feed, Trade trade)
            {
                if (trade.Type == TradeType.Buy)
                {
                    Balances[feed.Asset] += trade.Amount;
                    Balances[feed.CounterAsset] -= trade.Amount * trade.Price;
                }
                if (trade.Type == TradeType.Sell)
                {
                    Balances[feed.Asset] -= trade.Amount;
                    Balances[feed.CounterAsset] += trade.Amount * trade.Price;
                }
                Balances[feed.CounterAsset] -= trade.Fee;

                OnTradePending = true;
                lock (locker)
                    this.Trades.Add(trade);
            }
        }

        class SymbolFeed : ISymbolFeed
        {
            private bool onNewCandPending = false;
            private bool onTickPending = false;
            public SymbolFeed(string market, string asset, string counterAsset)
            {

            }

            internal TimeSerie<Candle> Ticks { get; private set; } = new TimeSerie<Candle>(100000);

            public event Action<ISymbolFeed> OnNewCandle;
            public event Action<ISymbolFeed> OnTick;

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string CounterAsset { get; private set; }

            public double Ask { get; private set; }
            public double Bid { get; private set; }

            public string Market { get; private set; }

            public double Spread { get; private set; }


            public double Volume24H { get; private set; }

            public Candle[] GetChartData(TimeSpan timeframe)
            {
                throw new NotImplementedException();
            }

            internal void AddNewCandle(Candle c)
            {
                var previousTime = Ticks.Tick.Time;

                //let's calculate the volume
                Volume24H += c.Volume;
                var delta = c.Time - previousTime;
                var timeAt24 = c.CloseTime - TimeSpan.FromHours(24);

                Ticks.SeekNearestPreceding(timeAt24 - delta);
                while (Ticks.Tick.Time < timeAt24)
                {
                    Volume24H -= Ticks.Tick.Volume;
                    Ticks.Next();
                }

                Ticks.AddRecord(c.Time, c);
                Ticks.SeekLast();
                Bid = c.Close;
                Ask = Bid + Spread;

                onNewCandPending = true;
            }

            internal void RaisePendingEvents()
            {
                if (onNewCandPending)
                {
                    OnNewCandle?.Invoke(this);
                    onNewCandPending = false;
                }
            }
        }

        class Order : IOrder
        {
            private static int idCounter = 0;
            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public double Rate { get; private set; }
            public double Amount { get; private set; }
            public string Id { get; private set; }

            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;

            public TradeType TradeType { get; private set; }
            public OrderType Type { get; private set; }

            public Order(string market, string symbol, TradeType tradeSide, OrderType orderType, double amount, double rate = 0)
            {
                Symbol = symbol;
                Market = market;
                TradeType = tradeSide;
                Type = orderType;
                Amount = amount;
                Rate = rate;
                Id = (idCounter++).ToString();
            }

        }

        class Trade : ITrade
        {
            public Trade(string market, string symbol, DateTime time, TradeType type, double price, double amount, double fee)
            {
                Market = market;
                Symbol = symbol;
                Date = time;
                Type = type;
                Price = price;
                Amount = amount;
                Fee = fee;
            }
            public double Amount { get; private set; }

            public DateTime Date { get; private set; }

            public double Fee { get; private set; }

            public string Market { get; private set; }

            public double Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeType Type { get; private set; }
        }

        class MarketOperation : IMarketOperation
        {
            public MarketOperationStatus Status { get; internal set; }
        }
    }


}
