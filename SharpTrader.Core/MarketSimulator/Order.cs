using System;

 

namespace SharpTrader.MarketSimulator
{ 
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
        public Order()
        {

        }
        public Order(string market, string symbol, DateTime time, TradeDirection tradeSide, OrderType orderType, decimal amount, decimal price, string clientId)
        {
            Id = (idCounter++).ToString();
            ClientId = clientId;
            Symbol = symbol;
            Market = market;
            TradeType = tradeSide;
            Type = orderType;
            Amount = amount;
            Price = price;
            Time = time;
        }
        public override string ToString()
        {
            return $"Order{{ Id: {this.Id}, ClientId: {this.ClientId} }}";
        }
    }
}
