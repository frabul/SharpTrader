using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS1998

namespace SharpTrader
{
    public partial class MultiMarketSimulator
    {
        public IEnumerable<ITrade> Trades => Markets.SelectMany(m => m.Trades);

        public decimal GetEquity(string baseAsset)
        {
            return Markets.Sum(m => m.GetEquity(baseAsset).Result.Result);
        }

        public class SymbolFeed : ISymbolFeed
        {
            public event Action<ISymbolFeed, IBaseData> OnData;

            private TimeSpan Resolution = TimeSpan.FromSeconds(60);
             
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

            public virtual async Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime)
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
                return newNavigator;
            }

            List<IBaseData> NewData = new List<IBaseData>(10);
            internal void AddNewData(IBaseData newMarketData)
            {
                Bid = newMarketData.Value;
                Ask = Bid + Spread;
                NewData.Add(newMarketData);

            }

            public void RaisePendingEvents(ISymbolFeed sender)
            {
                if (NewData.Count > 0)
                {
                    LastTick = NewData[NewData.Count - 1];
                    foreach (var data in NewData)
                    {
                        OnData?.Invoke(this, data); 
                    }
                    NewData.Clear();
                }
            }

            public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal amount, decimal price  )
            {
                Debug.Assert(amount > 0);
                Debug.Assert(price > 0);
                if (amount * price < 0.001m)
                    amount = 0.001m;
                return (price, amount);
            }

            public (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal amount, decimal price)
            {
                Debug.Assert(amount > 0);
                Debug.Assert(price > 0);
                if (amount * price < 0.001m)
                    amount = 0.001m / price;
                return (price, amount);
            }
        }

        class Order : IOrder
        {
            private static int idCounter = 0;
            public string Symbol { get; private set; }
            public string Market { get; private set; }
            public decimal Price { get; private set; }
            public decimal Amount { get; private set; }
            public string Id { get; private set; }
            public string ClientId { get; private set; }

            public OrderStatus Status { get; internal set; } = OrderStatus.Pending;

            public TradeDirection TradeType { get; private set; }

            public OrderType Type { get; private set; }

            public decimal Filled { get; set; }

            public DateTime Time { get; set; } 

            public bool IsClosed => this.Status >= OrderStatus.Cancelled;

            public Order(string market, string symbol, DateTime time, TradeDirection tradeSide, OrderType orderType, decimal amount, double rate, string clientId)
            {
                Id = (idCounter++).ToString();
                ClientId = clientId;
                Symbol = symbol;
                Market = market;
                TradeType = tradeSide;
                Type = orderType;
                Amount = amount;
                Price = (decimal)rate;
                Time = time;
            }
            public override string ToString()
            {
                return $"Order{{ Id: {this.Id}, ClientId: {this.ClientId} }}";
            }
        }

        class Trade : ITrade
        {
            private static long IdCounter = 0;
            public Trade(string market, string symbol, DateTime time, TradeDirection type, double price, decimal amount, Order order)
            {
                Market = market;
                Symbol = symbol;
                Time = time;
                Direction = type;
                Price = (decimal)price;
                Amount = amount;
                Order = order;
                Id = (IdCounter++).ToString();
            }
            public string Id { get; private set; }
            public decimal Amount { get; private set; }

            public DateTime Time { get; private set; }

            /// <summary>
            /// Commission paid
            /// </summary>
            public decimal Commission { get; set; }
            /// <summary>
            /// Asset used to pay the commission
            /// </summary>
            public string CommissionAsset { get; set; }
            public string Market { get; private set; }

            public decimal Price { get; private set; }

            public string Symbol { get; private set; }

            public TradeDirection Direction { get; private set; }

            public Order Order { get; private set; }

            public string ClientOrderId => Order.ClientId;

            public string OrderId => Order.Id;

            public override string ToString()
            {
                return $"Trade{{ Id: {Id}, Symbol:{Symbol}, Direction:{Direction}, Time:{Time} }}";
            }
        }

        class MarketOperation<T> : IMarketOperation<T>
        {
            public MarketOperationStatus Status { get; internal set; }
            public T Result { get; }
            public string ErrorInfo { get; internal set; }

            public bool IsSuccessful => Status == MarketOperationStatus.Completed;

            public MarketOperation(MarketOperationStatus status, T res)
            {
                Status = status;
                Result = res;
            }

            public static IMarketOperation<T> Completed(T val)
            {
                return new MarketOperation<T>(MarketOperationStatus.Completed, val);
            }
        }

        public class MarketConfiguration
        {
            public string MarketName { get; set; }
            public decimal MakerFee { get; set; }
            public decimal TakerFee { get; set; }
        }
    }
}
