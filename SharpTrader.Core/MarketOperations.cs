﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class Position
    {
        public MarginTradeType Type { get; private set; }
        public double Amount { get; private set; }
        public double Entry { get; private set; }
        public DateTime OpenTime { get; private set; }
        public double TakeProfit { get; set; }
        public double StopLoss { get; set; }
        public bool Closed { get; internal set; }

        internal Position(MarginTradeType type, double amount, double entry, DateTime time)
        {


            Type = type;
            Amount = amount;
            Entry = entry;
            OpenTime = time;
            if (type == MarginTradeType.Long)
            {
                TakeProfit = double.MaxValue;
                StopLoss = double.MinValue;
            }
            else
            {
                TakeProfit = double.MinValue;
                StopLoss = double.MaxValue;
            }
        }

        public override string ToString()
        {
            return string.Format("{0} EN:{1}   TP:{2}   SL{3}", Enum.GetName(Type.GetType(), Type), Entry, TakeProfit, StopLoss);
        }
    }

    public enum MarginTradeType
    {
        Long,
        Short,
    }

    public enum TradeDirection
    {
        Buy,
        Sell
    }

    public enum OrderType
    {
        Limit,
        Market,
        StopLoss,
        StopLossLimit,
        TakeProfit,
        TakeProfitLimit,
        LimitMaker,
        Unknown,
    }

    public enum OrderStatus
    {
        Pending,
        PartiallyFilled,
        Cancelled,
        PendingCancel,
        Rejected,
        Expired,
        Filled,
    }

    public interface IOrder
    {
        OrderStatus Status { get; }
        OrderType Type { get; }
        TradeDirection TradeType { get; }
        string Id { get; }
        decimal Amount { get; }
        decimal Filled { get; }
        decimal Price { get; }
        string Symbol { get; }
        string Market { get; }
        string ClientId { get; }
        DateTime Time { get; }
        bool IsClosed { get;   }
    }

    public class MarginTrade
    {
        public MarginTradeType Type { get; private set; }
        public double Entry { get; private set; }
        public double Close { get; private set; }
        public double Amount { get; private set; }
        public DateTime OpenTime { get; private set; }
        public DateTime CloseTime { get; private set; }

        internal MarginTrade(MarginTradeType type, double amount, double entry, double close, DateTime openTime, DateTime closeTime)
        {
            Type = type;
            Amount = amount;
            Entry = entry;
            Close = close;
            OpenTime = openTime;
            CloseTime = closeTime;
        }

        public double Earnings
        {
            get
            {
                if (Type == MarginTradeType.Long)
                    return (Close - Entry) * Amount;
                else
                    return (Entry - Close) * Amount;
            }
        }

    }
    [Obfuscation(Exclude = true)]
    public interface ITrade
    {
        string Id { get; }
        string Market { get; }
        string Symbol { get; }
        decimal Amount { get; }
        decimal Price { get; }
        decimal Commission { get; }
        string CommissionAsset { get; }
        TradeDirection Direction { get; }
        string ClientOrderId { get; }
        string OrderId { get; }
        DateTime Time { get; }
    }
}
