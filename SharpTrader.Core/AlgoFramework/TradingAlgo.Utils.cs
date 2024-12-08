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
                    Logger.Error("{Symbol} - symbol feed not found while selling", symbolKey);
                    return;
                }
                var amount = Market.GetFreeBalance(feed.Symbol.Asset);
                var adj = feed.GetOrderAmountAndPriceRoundedDown(amount, (decimal)feed.Bid);

                if (adj.amount > 0)
                {
                    Logger.Information("Try selling {Amount} {Symbol} @ {Price} because {Reason}.", adj.amount, symbolKey, adj.price, reason);
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
                        Logger.Error("Error while selling {Amount} {Symbol} @ {Price}: {ErrInfo}.", adj.amount, symbolKey, adj.price, request.ErrorInfo);
                    }
                }
                else
                {
                    Logger.Error("Unable to sell {Symbol} because amount ( {Amount} ) is too low", symbolKey, amount);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during try sell {Symbol}: {Message}", symbolKey, ex.Message);
            }

        }

        public async Task<(IOrder order, bool amountRemainingLow, bool OrderError)> TryLiquidateOperation(Operation op, string reason)
        {
            var logger = Logger.ForContext("Symbol", op.Symbol);
            (IOrder order, bool amountRemainingLow, bool OrderError) result = default;
            await Executor.CancelAllOrders(op);
            logger.Information("{OperationId} - liquidation because {Reason}", op.Id, reason);
            if (op.AmountRemaining < 0)
            {
                logger.Warning("{OperationId} - liquidation requested but AmountRemaining < 0", op.Id);
                return result;
            }

            var symData = this.GetSymbolData(op.Symbol);
            var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, (decimal)symData.Feed.Bid);

            if (adj.amount <= 0)
            {
                result.amountRemainingLow = true;
                logger.Warning("{OperationId} - unable to liquidate because amount remaining is too low", op.Id);
                return result;
            }

            adj = ClampOrderAmount(symData, op.ExitTradeDirection, adj);
            if (adj.amount <= 0)
            {
                logger.Warning("{OperationId} - unable to liquidate because not enough free balance", op.Id); ;
                return result;
            }

            //immediatly liquidate everything with a market order 
            var orderInfo = new OrderInfo()
            {
                Symbol = op.Symbol.Key,
                Type = OrderType.Market,
                Effect = this.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                Amount = adj.amount,
                ClientOrderId = op.GetNewOrderId(),
                Direction = op.ExitTradeDirection
            };

            logger.Debug("{OperationId} - try market exit {OrderDirection} {Symbol} {Amount}@{Price}.", op.Id, orderInfo.Direction, orderInfo.Symbol, orderInfo.Amount, symData.Feed.Bid);
            var request = await Market.PostNewOrder(orderInfo);
            if (request.IsSuccessful)
                result.order = request.Result;
            else
            {
                result.OrderError = true;
                logger.Error("{OperationId} - error executing market order because {Reason}.", op.Id, request.ErrorInfo);
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
