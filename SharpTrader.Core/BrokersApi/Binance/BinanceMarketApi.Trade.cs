using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.WebSocket;
using LiteDB;
using System;

namespace SharpTrader.BrokersApi.Binance
{
    class Trade : ITrade
    {
        public Trade()
        {

        }
        public Trade(string symbol, AccountTradeReponse tr)
        {
            Market = "Binance";
            Symbol = symbol;
            Direction = tr.IsBuyer ? TradeDirection.Buy : TradeDirection.Sell;
            Price = tr.Price;
            Amount = tr.Quantity;
            Commission = tr.Commission;
            CommissionAsset = tr.CommissionAsset;
            Time = tr.Time;
            OrderId = tr.OrderId;
            TradeId = tr.Id;
            Id = Symbol + TradeId;
        }

        public Trade(BinanceTradeOrderData tr)
        {
            Market = "Binance";
            Symbol = tr.Symbol;
            Direction = tr.Side == OrderSide.Buy ? TradeDirection.Buy : TradeDirection.Sell;
            Price = tr.PriceOfLastFilledTrade;
            Amount = tr.QuantityOfLastFilledTrade;
            Commission = Commission;
            CommissionAsset = CommissionAsset;
            Time = tr.TimeStamp;
            OrderId = tr.OrderId;
            ClientOrderId = tr.NewClientOrderId;
            TradeId = tr.TradeId;
            Id = Symbol + TradeId;
        }
        [BsonId]
        public string Id { get; set; }
        public long TradeId { get; set; }
        public long OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public decimal Amount { get; set; }
        public decimal Commission { get; set; }
        public string Market { get; set; }
        public decimal Price { get; set; }
        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public string CommissionAsset { get; set; }
        public DateTime Time { get; set; }

        [BsonIgnore]
        string ITrade.OrderId => Symbol + OrderId;

        public override string ToString()
        {
            return $"Trade{{ Id: {Id} - {Symbol}, Direction:{Direction}, Time:{Time} - Price:{this.Price:0.########} - QAmount:{this.Amount*Price:0.######} }}";
        }
        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }
    }
}
