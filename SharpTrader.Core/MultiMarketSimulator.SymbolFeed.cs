using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SymbolsTable = System.Collections.Generic.Dictionary<string, (string Asset, string Quote)>;
namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        class Market : IMarketApi
        {
            object locker = new object();
            private Dictionary<string, double> _Balances = new Dictionary<string, double>();
            private List<Trade> _Trades = new List<Trade>();
            private Dictionary<string, SymbolFeed> SymbolsFeed = new Dictionary<string, SymbolFeed>();
            private List<Order> PendingOrders = new List<Order>();
            private List<Order> ClosedOrders = new List<Order>();
            private List<ITrade> TradesToSignal = new List<ITrade>();
            private SymbolsTable SymbolsTable;

            public string MarketName { get; private set; }
            public double MakerFee { get; private set; } = 0.0015;
            public double TakerFee { get; private set; } = 0.0025;
            public DateTime Time { get; internal set; }


            public event Action<IMarketApi, ITrade> OnNewTrade;

            public IEnumerable<ISymbolFeed> Feeds => SymbolsFeed.Values;
            public IEnumerable<ISymbolFeed> ActiveFeeds => SymbolsFeed.Values;
            public IEnumerable<ITrade> Trades => this._Trades;
            public Market(string name, double makerFee, double takerFee, string dataDir)
            {
                MarketName = name;
                MakerFee = makerFee;
                TakerFee = takerFee;
                var text = System.IO.File.ReadAllText(dataDir + "BinanceSymbolsTable.json");
                SymbolsTable = Newtonsoft.Json.JsonConvert.DeserializeObject<SymbolsTable>(text);
            }

            public ISymbolFeed GetSymbolFeed(string symbol)
            {
                var feedFound = SymbolsFeed.TryGetValue(symbol, out SymbolFeed feed);
                if (!feedFound)
                {
                    (var asset, var counterAsset) = SymbolsTable[symbol];
                    feed = new SymbolFeed(this.MarketName, symbol, asset, counterAsset);
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
            public double GetBalance(string asset)
            {
                _Balances.TryGetValue(asset, out double res);
                return res;
            }

            public (string Symbol, double balance)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();

            internal void RaisePendingEvents()
            {
                List<ITrade> trades;
                lock (locker)
                {
                    trades = new List<ITrade>(TradesToSignal);
                    TradesToSignal.Clear();
                }
                foreach (var trade in TradesToSignal)
                {
                    this.OnNewTrade?.Invoke(this, trade);
                }

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
                if (!_Balances.ContainsKey(feed.Asset))
                    _Balances.Add(feed.Asset, 0);
                if (!_Balances.ContainsKey(feed.QuoteAsset))
                    _Balances.Add(feed.QuoteAsset, 0);
                if (trade.Type == TradeType.Buy)
                {
                    _Balances[feed.Asset] += trade.Amount;
                    _Balances[feed.QuoteAsset] -= trade.Amount * trade.Price;
                }
                if (trade.Type == TradeType.Sell)
                {
                    _Balances[feed.Asset] -= trade.Amount;
                    _Balances[feed.QuoteAsset] += trade.Amount * trade.Price;
                }
                _Balances[feed.QuoteAsset] -= trade.Fee;

                lock (locker)
                    this._Trades.Add(trade);
            }
        }

        class SymbolFeed : ISymbolFeed
        {
            public event Action<ISymbolFeed> OnTick;
            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            public List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)> NewCandleSubscribers =
                new List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)>();

            private bool onTickPending = false;

            private List<DerivedChart> DerivedTicks = new List<DerivedChart>(20);
            private object Locker = new object();

            public TimeSerie<ICandlestick> Ticks { get; set; } = new TimeSerie<ICandlestick>();

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string QuoteAsset { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }

            private class DerivedChart
            {
                public TimeSpan Timeframe;
                public TimeSerie<ICandlestick> Ticks;
                public Candlestick FormingCandle;
            }

            public SymbolFeed(string market, string symbol, string asset, string quoteAsset)
            {
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
            }


            public TimeSerieNavigator<ICandlestick> GetNavigator(TimeSpan timeframe)
            {
                if (BaseTimeframe == timeframe)
                {
                    return Ticks;
                }
                else
                {
                    var der = DerivedTicks.Where(dt => dt.Timeframe == timeframe).FirstOrDefault();
                    if (der == null)
                    {
                        der = new DerivedChart()
                        {
                            Timeframe = timeframe,
                            FormingCandle = null,
                            Ticks = new TimeSerie<ICandlestick>()
                        };

                        //we need to initialize it with all the data that we have
                        var tticks = new TimeSerieNavigator<ICandlestick>(this.Ticks);
                        while (tticks.Next())
                        {
                            var newCandle = tticks.Tick;
                            AddTickToDerivedChart(der, newCandle);

                        }
                        DerivedTicks.Add(der);
                    }
                    return new TimeSerieNavigator<ICandlestick>(der.Ticks);
                }

            }

            private void AddTickToDerivedChart(DerivedChart der, ICandlestick newCandle)
            {
                if (der.FormingCandle == null)
                    der.FormingCandle = new Candlestick(newCandle, der.Timeframe);

                if (der.FormingCandle.CloseTime <= newCandle.OpenTime)
                {
                    //old candle is formed
                    der.Ticks.AddRecord(der.FormingCandle);
                    der.FormingCandle = new Candlestick(newCandle, der.Timeframe);
                }
                else
                {
                    der.FormingCandle.Merge(newCandle);
                    if (der.FormingCandle.CloseTime < newCandle.CloseTime)
                    {

                        der.Ticks.AddRecord(der.FormingCandle);
                        der.FormingCandle = null;

                    }
                }
            }

            internal void AddNewCandle(Candlestick c)
            {
                BaseTimeframe = c.CloseTime - c.OpenTime;

                var previousTime = c.OpenTime;
                Volume24H += c.Volume;
                //let's calculate the volume
                if (Ticks.Count > 0)
                {
                    Ticks.PositionPush();
                    Ticks.SeekLast();
                    previousTime = Ticks.Tick.CloseTime;
                    var delta = c.CloseTime - previousTime;
                    var timeAt24 = c.CloseTime - TimeSpan.FromHours(24);
                    var removeStart = timeAt24 - delta;
                    if (removeStart < Ticks.FirstTickTime)
                        Ticks.SeekFirst();
                    else
                        Ticks.SeekNearestBefore(timeAt24 - delta);

                    while (Ticks.Tick.OpenTime < timeAt24)
                    {
                        Volume24H -= Ticks.Tick.Volume;
                        Ticks.Next();
                    }
                    Ticks.PositionPop();
                }

                Ticks.AddRecord(c);

                Bid = c.Close;
                Ask = Bid + Spread;

                foreach (var derived in DerivedTicks)
                {
                    AddTickToDerivedChart(derived, c);
                }
                onTickPending = true;
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
