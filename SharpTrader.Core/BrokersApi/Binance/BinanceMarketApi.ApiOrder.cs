using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.WebSocket;
using LiteDB;
using System;
using System.Collections.Generic;
using be = BinanceExchange.API;

namespace SharpTrader.BrokersApi.Binance
{  
    class Order : IOrder
    {
        [BsonId]
        public string Id { get; set; }
        public string Symbol { get; set; }
        public long OrderId { get; set; }
        public decimal Filled { get; set; }
        public string Market { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public string ClientId { get; set; }
        public TradeDirection TradeType { get; set; }
        public OrderType Type { get; set; }
        public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
        public List<long> ResultingTrades { get; set; } = new List<long>();
        public DateTime Time { get; set; }

        public bool IsClosed => Status >= OrderStatus.Cancelled;

        public Order() { }

        public Order(AcknowledgeCreateOrderResponse binanceOrder)
        {
            OrderId = binanceOrder.OrderId;
            ClientId = binanceOrder.ClientOrderId;
            Symbol = binanceOrder.Symbol;
            Time = binanceOrder.TransactionTime;
            Market = "Binance";
            Id = Symbol + OrderId;
        }

        public Order(ResultCreateOrderResponse binanceOrder)
        {
            OrderId = binanceOrder.OrderId;
            ClientId = binanceOrder.ClientOrderId;
            Symbol = binanceOrder.Symbol;
            Market = "Binance";

            TradeType = binanceOrder.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(binanceOrder.Type);
            Amount = binanceOrder.OriginalQuantity;
            Price = binanceOrder.Price;
            Status = GetStatus(binanceOrder.Status);
            Filled = binanceOrder.ExecutedQuantity;
            Id = Symbol + OrderId;
            Time = binanceOrder.TransactionTime;
        }

        public Order(OrderResponse or)
        {
            Symbol = or.Symbol;
            Market = "Binance";
            TradeType = or.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(or.Type);
            Amount = or.OriginalQuantity;
            Price = or.Price;
            Status = GetStatus(or.Status);
            Filled = or.ExecutedQuantity;
            OrderId = or.OrderId;
            Id = Symbol + OrderId;
            ClientId = or.ClientOrderId;
            Time = or.Time;
        }

        public Order(BinanceTradeOrderData bo)
        {
            OrderId = bo.OrderId;
            Symbol = bo.Symbol;
            Market = "Binance";
            TradeType = bo.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Type = GetOrderType(bo.Type);
            Amount = bo.Quantity;
            Price = bo.Price;
            Status = GetStatus(bo.OrderStatus);
            Filled = bo.AccumulatedQuantityOfFilledTradesThisOrder;
            Id = Symbol + OrderId;
            ClientId = bo.NewClientOrderId;
            Time = bo.EventTime;
        }

        private static OrderType GetOrderType(be.Enums.OrderType type)
        {
            switch (type)
            {
                case be.Enums.OrderType.Limit:
                    return OrderType.Limit;
                case be.Enums.OrderType.Market:
                    return OrderType.Market;
                case be.Enums.OrderType.StopLoss:
                    return OrderType.StopLoss;
                case be.Enums.OrderType.StopLossLimit:
                    return OrderType.StopLossLimit;
                case be.Enums.OrderType.TakeProfit:
                    return OrderType.TakeProfit;
                case be.Enums.OrderType.TakeProfitLimit:
                    return OrderType.TakeProfitLimit;
                case be.Enums.OrderType.LimitMaker:
                    return OrderType.LimitMaker;
                default:
                    return OrderType.Unknown;
            }
        }

        private static OrderStatus GetStatus(be.Enums.OrderStatus status)
        {
            switch (status)
            {
                case be.Enums.OrderStatus.New:
                    return OrderStatus.Pending;
                case be.Enums.OrderStatus.PartiallyFilled:
                    return OrderStatus.PartiallyFilled;
                case be.Enums.OrderStatus.Filled:
                    return OrderStatus.Filled;
                case be.Enums.OrderStatus.Cancelled:
                    return OrderStatus.Cancelled;
                case be.Enums.OrderStatus.PendingCancel:
                    return OrderStatus.PendingCancel;
                case be.Enums.OrderStatus.Rejected:
                    return OrderStatus.Rejected;
                case be.Enums.OrderStatus.Expired:
                    return OrderStatus.Expired;
                default:
                    throw new Exception("Unknown order status");
            }
        }

        internal void Update(Order order)
        {
            this.Amount = order.Amount;
            this.Filled = order.Filled;
            this.Status = order.Status;
            this.Filled = order.Filled;
        }

        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }
    }
}
