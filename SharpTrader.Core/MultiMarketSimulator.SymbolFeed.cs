﻿using System;
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
            object LockObject = new object();
            private Dictionary<string, decimal> _Balances = new Dictionary<string, decimal>();
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
                    lock (LockObject)
                        SymbolsFeed.Add(symbol, feed);
                }
                return feed;
            }

            public IMarketOperation<IOrder> LimitOrder(string symbol, TradeType type, decimal amount, decimal rate)
            {
                var order = new Order(this.MarketName, symbol, type, OrderType.Limit, amount, (double)rate);
                lock (LockObject)
                    this.PendingOrders.Add(order);
                return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };

            }

            public IMarketOperation<IOrder> MarketOrder(string symbol, TradeType type, decimal amount)
            {
                lock (LockObject)
                {
                    var feed = SymbolsFeed[symbol];
                    var price = type == TradeType.Buy ? feed.Ask : feed.Bid;
                    var order = new Order(this.MarketName, symbol, type, OrderType.Market, amount, price);
                    order.Filled = order.Amount;
                    var trade = new Trade(
                        this.MarketName, symbol, this.Time,
                        type, price, amount,
                        amount * (decimal)(this.TakerFee * price), order);

                    RegisterTrade(feed, trade);
                    this.ClosedOrders.Add(order);
                    return new MarketOperation<IOrder>(MarketOperationStatus.Completed, order) { };
                }


            }

            public decimal GetBalance(string asset)
            {
                _Balances.TryGetValue(asset, out decimal res);
                return res;
            }

            public (string Symbol, decimal balance)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();

            public IEnumerable<IOrder> OpenOrders
            {
                get
                {
                    lock (LockObject)
                        return PendingOrders.ToArray();
                }
            }

            internal void RaisePendingEvents()
            {
                List<ITrade> trades;
                lock (LockObject)
                {
                    trades = new List<ITrade>(TradesToSignal);
                    TradesToSignal.Clear();
                }
                foreach (var trade in trades)
                {
                    this.OnNewTrade?.Invoke(this, trade);
                }

                foreach (var feed in SymbolsFeed.Values)
                    feed.RaisePendingEvents(feed);
            }

            internal void ResolveOrders()
            {
                //resolve orders/trades 
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        var feed = SymbolsFeed[order.Symbol];
                        if (order.Type == OrderType.Limit)
                        {
                            var willBuy = (order.TradeType == TradeType.Buy && feed.Ticks.LastTick.Low + feed.Spread <= order.Rate);
                            var willSell = (order.TradeType == TradeType.Sell && feed.Ticks.LastTick.High >= order.Rate);

                            if (willBuy || willSell)
                            {
                                var trade = new Trade(
                                    market: this.MarketName,
                                    symbol: feed.Symbol,
                                    time: feed.Ticks.LastTick.OpenTime.AddSeconds(feed.Ticks.LastTick.Timeframe.Seconds / 2),
                                    price: order.Rate,
                                    amount: order.Amount,
                                    type: order.TradeType,
                                    fee: order.Amount * (decimal)(this.MakerFee * order.Rate),
                                    order: order
                                );
                                RegisterTrade(feed, trade);
                                order.Status = OrderStatus.Filled;
                                order.Filled = order.Amount;
                                PendingOrders.RemoveAt(i--);
                            }
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
                lock (LockObject)
                {
                    if (!_Balances.ContainsKey(feed.Asset))
                        _Balances.Add(feed.Asset, 0);
                    if (!_Balances.ContainsKey(feed.QuoteAsset))
                        _Balances.Add(feed.QuoteAsset, 0);
                    if (trade.Type == TradeType.Buy)
                    {
                        _Balances[feed.Asset] += Convert.ToDecimal(trade.Amount);
                        _Balances[feed.QuoteAsset] -= Convert.ToDecimal(trade.Amount * (decimal)trade.Price);
                    }
                    if (trade.Type == TradeType.Sell)
                    {
                        _Balances[feed.Asset] -= Convert.ToDecimal(trade.Amount);
                        _Balances[feed.QuoteAsset] += Convert.ToDecimal(trade.Amount * (decimal)trade.Price);
                    }
                    _Balances[feed.QuoteAsset] -= Convert.ToDecimal(trade.Fee);


                    this._Trades.Add(trade);
                    TradesToSignal.Add(trade);
                }

            }

            public decimal GetNormalizedPortfolioValue(string asset)
            {
                decimal val = 0;
                foreach (var kv in _Balances)
                {
                    if (kv.Key == asset)
                        val += kv.Value;
                    else if (kv.Value != 0)
                    {
                        var symbol = (kv.Key + asset);
                        if (SymbolsFeed.ContainsKey(symbol))
                        {
                            var feed = SymbolsFeed[symbol];
                            val += ((decimal)feed.Ask * kv.Value);
                        }
                        var sym2 = asset + kv.Key;
                        if (SymbolsFeed.ContainsKey(sym2))
                        {
                            var feed = SymbolsFeed[sym2];
                            val += (kv.Value / (decimal)feed.Bid);
                        }
                    }
                }
                return val;
            }

            public (decimal min, decimal step) GetMinTradable(string tradeSymbol)
            {
                return (0, 0);
            }

            public void OrderCancel(string id)
            {
                lock (LockObject)
                {
                    for (int i = 0; i < PendingOrders.Count; i++)
                    {
                        var order = PendingOrders[i];
                        if (order.Id == id)
                        {
                            PendingOrders.RemoveAt(i--);
                            order.Status = OrderStatus.Cancelled;
                            ClosedOrders.Add(order);
                        }

                    }
                }
            }


            public decimal GetSymbolPrecision(string symbol)
            {
                return 0.0000000001m;
            }

            public decimal GetMinNotional(string asset)
            {
                return 0;
            }

            internal void AddBalance(string asset, decimal amount)
            {
                if (!_Balances.ContainsKey(asset))
                    _Balances.Add(asset, 0);
                _Balances[asset] += amount;
            }
        }



        class SymbolFeed : SymbolFeedBoilerplate, ISymbolFeed
        {

            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            public List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)> NewCandleSubscribers =
                new List<(TimeSpan Timeframe, List<WeakReference<IChartDataListener>> Subs)>();


            private List<DerivedChart> DerivedTicks = new List<DerivedChart>(20);
            private object Locker = new object();

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string QuoteAsset { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }


            public SymbolFeed(string market, string symbol, string asset, string quoteAsset)
            {
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
            }

            internal void AddNewCandle(Candlestick newCandle)
            {
                BaseTimeframe = newCandle.CloseTime - newCandle.OpenTime;

                var previousTime = newCandle.OpenTime;
                Volume24H += newCandle.Volume;
                //let's calculate the volume
                if (Ticks.Count > 0)
                {
                    Ticks.PositionPush();
                    Ticks.SeekLast();
                    previousTime = Ticks.Tick.CloseTime;
                    var delta = newCandle.CloseTime - previousTime;
                    var timeAt24 = newCandle.CloseTime - TimeSpan.FromHours(24);
                    var removeStart = timeAt24 - delta;
                    if (removeStart < Ticks.FirstTickTime)
                        Ticks.SeekFirst();
                    else
                        Ticks.SeekNearestBefore(timeAt24 - delta);

                    //todo 
                    //while (Ticks.Tick.OpenTime < timeAt24)
                    //{
                    //    Volume24H -= Ticks.Tick.Volume;
                    //    Ticks.Next();
                    //}
                    Ticks.PositionPop();
                }

                Ticks.AddRecord(newCandle);

                Bid = newCandle.Close;
                Ask = Bid + Spread;

                UpdateDerivedCharts(newCandle);
                SignalTick();
            }

        }

        class Order : IOrder
        {
            private static int idCounter = 0;
            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public double Rate { get; private set; }
            public decimal Amount { get; private set; }
            public string Id { get; private set; }

            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;

            public TradeType TradeType { get; private set; }
            public OrderType Type { get; private set; }
            public decimal Filled { get; set; }
            public Order(string market, string symbol, TradeType tradeSide, OrderType orderType, decimal amount, double rate = 0)
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
            public Trade(string market, string symbol, DateTime time, TradeType type, double price, decimal amount, decimal fee, IOrder order)
            {
                Market = market;
                Symbol = symbol;
                Date = time;
                Type = type;
                Price = price;
                Amount = amount;
                Fee = fee;
                Order = order;
            }
            public decimal Amount { get; private set; }

            public DateTime Date { get; private set; }

            public decimal Fee { get; private set; }

            public string Market { get; private set; }

            public double Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeType Type { get; private set; }

            public IOrder Order { get; private set; }
        }
        class MarketOperation<T> : IMarketOperation<T>
        {
            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }
            public MarketOperationStatus Status { get; internal set; }
            public T Result { get; }
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
