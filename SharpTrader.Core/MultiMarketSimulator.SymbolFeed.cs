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

            public string MarketName { get; private set; }
            public double MakerFee { get; private set; } = 0.0015;
            public double TakerFee { get; private set; } = 0.0025;
            public DateTime Time { get; internal set; }

            public IEnumerable<ISymbolFeed> Feeds => SymbolsFeed.Values;


            public Market(string name, double makerFee, double takerFee)
            {
                MarketName = name;
                MakerFee = makerFee;
                TakerFee = takerFee;
            }

            public ISymbolFeed GetSymbolFeed(string symbol)
            {
                var feedFound = SymbolsFeed.TryGetValue(symbol, out SymbolFeed feed);
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
                    feed = new SymbolFeed(this.MarketName, asset, counterAsset);
                    lock (locker)
                        SymbolsFeed.Add(symbol, feed);
                }
                return feed;
            }

            public IMarketOperation LimitOrder(string symbol, TradeType type, double amount, double rate)
            {

                var order = new Order(this.MarketName, symbol, type, OrderType.Limit, amount, rate);
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
                    var order = new Order(this.MarketName, symbol, type, OrderType.Market, amount, rate);

                    var trade = new Trade(this.MarketName, symbol, this.Time, type, rate, amount, this.TakerFee * amount * rate);
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
                                    market: this.MarketName,
                                    symbol: feed.Symbol,
                                    time: feed.Ticks.Tick.OpenTime,
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

            internal void AddNewCandle(SymbolFeed feed, Candlestick tick)
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
            public event Action<ISymbolFeed> OnTick;
            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            public List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)> NewCandleSubscribers =
                new List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)>();

            private bool onTickPending = false;
            public TimeSerie<ICandlestick> Ticks { get; set; } = new TimeSerie<ICandlestick>(100000);
            private List<(TimeSpan Timeframe, TimeSerie<ICandlestick> Ticks)> DerivedTicks = new List<(TimeSpan Timeframe, TimeSerie<ICandlestick> Ticks)>(30);
            private object Locker = new object();

            public SymbolFeed(string market, string asset, string counterAsset)
            {

            }

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string CounterAsset { get; private set; }

            public double Ask { get; private set; }
            public double Bid { get; private set; }

            public string Market { get; private set; }

            public double Spread { get; private set; }


            public double Volume24H { get; private set; }

            public TimeSerie<ICandlestick> GetChartData(TimeSpan timeframe)
            {
                if (BaseTimeframe == timeframe)
                {
                    return Ticks;
                }
                else
                {
                    (var tf, var derived) = DerivedTicks.Where(dt => dt.Timeframe == timeframe).FirstOrDefault();
                    if (derived == null)
                    {

                        derived = new TimeSerie<ICandlestick>(100000);
                        //we need to initialize it with all the data that we have
                        var tticks = new TimeSerie<ICandlestick>(Ticks);
                        while (tticks.Next())
                        {
                            var newCandle = tticks.Tick;
                            AddTickToDerivedTimeframe(timeframe, derived, newCandle);
                        }
                        DerivedTicks.Add((timeframe, derived));
                    }
                    return derived;
                }

            }

            private static void AddTickToDerivedTimeframe(TimeSpan timeframe, TimeSerie<ICandlestick> derived, ICandlestick newCandle)
            {
                if (derived.LastTick.CloseTime >= newCandle.CloseTime)
                    ((Candlestick)derived.Tick).Merge(newCandle);
                else
                    derived.AddRecord(new Candlestick(newCandle, timeframe));
            }

            internal void AddNewCandle(Candlestick c)
            {
                BaseTimeframe = c.CloseTime - c.OpenTime;
                var previousTime = Ticks.Tick.CloseTime;

                //let's calculate the volume
                Volume24H += c.Volume;
                var delta = c.CloseTime - previousTime;
                var timeAt24 = c.CloseTime - TimeSpan.FromHours(24);

                Ticks.SeekNearestPreceding(timeAt24 - delta);
                while (Ticks.Tick.OpenTime < timeAt24)
                {
                    Volume24H -= Ticks.Tick.Volume;
                    Ticks.Next();
                }

                Ticks.AddRecord(c);

                Bid = c.Close;
                Ask = Bid + Spread;

                foreach (var serie in DerivedTicks)
                {
                    AddTickToDerivedTimeframe(BaseTimeframe, serie.Ticks, c);
                }
            }

            internal void RaisePendingEvents()
            {
                if (onTickPending)
                {
                    OnTick?.Invoke(this);
                    onTickPending = false;
                }
            }

            public void SubscribeToNewCandle(IChartDataListener subscriber, TimeSpan timeframe)
            {
                lock (Locker)
                {
                    var (_, subs) = NewCandleSubscribers.FirstOrDefault(el => el.Timeframe == timeframe);
                    if (subs == null)
                        NewCandleSubscribers.Add((timeframe, subs = new List<WeakReference<IChartDataListener>>()));

                    for (int i = 0; i < subs.Count; i++)
                    {
                        if (!subs[i].TryGetTarget(out var obj))
                            subs.RemoveAt(i--);
                    }

                    if (!subs.Any(it => it.TryGetTarget(out var sub) && sub.Equals(subscriber)))
                    {
                        subs.Add(new WeakReference<IChartDataListener>(subscriber));
                    }
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

        class MarketConfiguration
        {
            public string MarketName { get; set; }
            public double MakerFee { get; set; }
            public double TakerFee { get; set; }
        }

        class SymbolConfiguration
        {
            public string SymbolName { get; set; }
            public string MarketName { get; set; }
            public string Spread { get; set; }
        }

    }


}
