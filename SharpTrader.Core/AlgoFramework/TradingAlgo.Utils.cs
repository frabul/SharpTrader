﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract partial class TradingAlgo : TraderBot
    {

        public async Task TrySellAllAmount(string symbolKey, string reason)
        {
            try
            {
                var feed = await Market.GetSymbolFeedAsync(symbolKey);
                if (feed == null)
                {
                    Logger.Error($"Error while selling {symbolKey}: symbol not found.");
                    return;
                }
                var amount = Market.GetFreeBalance(feed.Symbol.Asset);
                var adj = feed.GetOrderAmountAndPriceRoundedDown(amount, (decimal)feed.Bid);
                Logger.Info($"Try selling {adj.amount} {symbolKey } @ {adj.price} because {reason}.");
                if (adj.amount > 0)
                {
                    //immediatly liquidate everything with a market order 

                    var orderInfo = new OrderInfo()
                    {
                        Symbol = symbolKey,
                        Type = OrderType.Market,
                        Effect = this.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                        Amount = adj.amount,
                        ClientOrderId = null,
                        Direction = TradeDirection.Sell
                    };
                    var request = await Market.PostNewOrder(orderInfo);
                    if (!request.IsSuccessful)
                    {
                        Logger.Info($"Error while selling {adj.amount} {symbolKey } @ {adj.price}.");
                    }
                }
                else
                {
                    Logger.Error($"Unable to sell {symbolKey } because amount ( {amount} ) is too low");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during try sell {symbolKey}: {ex.Message}");
            }

        }

        public async Task<(IOrder order, bool amountRemainingLow, bool OrderError)> TryLiquidateOperation(Operation op, string reason)
        {
            (IOrder order, bool amountRemainingLow, bool OrderError) result = default;
            await Executor.CancelAllOrders(op);
            if (op.AmountRemaining > 0)
            {
                Logger.Info($"Liquidating operation {op.ToString("c")} because: " + reason);

                var symData = this.GetSymbolData(op.Symbol);
                var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, (decimal)symData.Feed.Bid);
                adj = ClampOrderAmount(symData, op.ExitTradeDirection, adj);

                if (adj.amount > 0)
                {
                    //immediatly liquidate everything with a market order 
                    Logger.Info($"Try market exit amount:{adj.amount} - price: {adj.price}.");
                    var orderInfo = new OrderInfo()
                    {
                        Symbol = op.Symbol.Key,
                        Type = OrderType.Market,
                        Effect = this.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                        Amount = adj.amount,
                        ClientOrderId = op.GetNewOrderId(),
                        Direction = op.ExitTradeDirection
                    };
                    var request = await Market.PostNewOrder(orderInfo);
                    if (request.IsSuccessful)
                        result.order = request.Result;
                    else
                    {
                        result.OrderError = true;
                        Logger.Error($"Error liquidating operation {op} - symbol {op.Symbol.Key} - amount:{adj.amount} - price: {adj.price}");
                    }
                }
                else
                {
                    result.amountRemainingLow = true;
                    Logger.Info($"Unable to liquidate operation {op} because amount remaining is too low");
                }
            }
            return result;
        }

        public (decimal price, decimal amount) ClampOrderAmount(SymbolData symData, TradeDirection tradeDirection, (decimal price, decimal amount) adj)
        {
            if (adj.amount > 0)
            {
                if (tradeDirection == TradeDirection.Buy)
                {
                    var freeAmount = Market.GetFreeBalance(symData.Symbol.QuoteAsset);
                    if (adj.amount * adj.price > freeAmount)
                    {
                        adj.amount = 0.99m * freeAmount / adj.price;
                        if (adj.amount > 0)
                            adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(adj.amount, adj.price);
                    }
                }
                else
                {

                    var freeAmount = Market.GetFreeBalance(symData.Symbol.Asset);
                    if (adj.amount > freeAmount)
                    {
                        if (!symData.Symbol.IsMarginTadingAllowed || !this.DoMarginTrading)
                            adj.amount = freeAmount;
                    }
                    if (adj.amount > 0)
                        adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(adj.amount, adj.price);
                }
            }
            return adj;
        }

    }
}
