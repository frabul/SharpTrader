﻿using System;
using System.Reflection;

namespace SharpTrader.MarketSimulator
{ 
    public class Trade : ITrade
    {
        private static long IdCounter = 0;
        public Trade()
        {

        }
        public Trade(string market, string symbol, DateTime time, TradeDirection type, decimal price, decimal amount, Order order)
        {
            Market = market;
            Symbol = symbol;
            Time = time;
            Direction = type;
            Price = price;
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
            return $"{{ Id: {Id} - Symbol:{Symbol} {Direction} {Price} at {Time} }}";
        }
    }
}
