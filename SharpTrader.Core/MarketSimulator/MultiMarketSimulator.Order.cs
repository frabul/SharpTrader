﻿using System;

#pragma warning disable CS1998

namespace SharpTrader
{
    public partial class MultiMarketSimulator
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
    }
}
