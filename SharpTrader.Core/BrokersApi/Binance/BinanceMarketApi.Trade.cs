using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.WebSocket;
using LiteDB;
using System;
using System.Reflection;

namespace SharpTrader.BrokersApi.Binance
{
    [Obfuscation(Exclude = true)]
    class Trade : ITrade
    {
        public Trade()
        {

        }

        public Trade(AccountTradeReponse tr)
        {
            Market = "Binance";
            Symbol = tr.Symbol;
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
            Commission = tr.Commission;
            CommissionAsset = tr.AssetCommissionTakenFrom;
            Time = tr.TimeStamp;
            OrderId = tr.OrderId;
            ClientOrderId = !String.IsNullOrEmpty(tr.OriginalClientOrderId) ? tr.OriginalClientOrderId : tr.NewClientOrderId;
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
            if (string.IsNullOrEmpty(ClientOrderId))
                return $"{{ {Id} - {Time} - {Symbol} {Direction} {Amount:0.######} @ {this.Price:0.########} - QAmount:{this.Amount * Price:0.######} }}";
            else
                return $"{{ {ClientOrderId}- {Time} - {Symbol} {Direction} {Amount:0.######} @ {this.Price:0.########} - QAmount:{this.Amount * Price:0.######} }}";
        }
        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }
    }
}
