using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract partial class TradingAlgo : TraderBot
    {
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
                    var request =
                        await Market.MarketOrderAsync(
                            op.Symbol.Key, op.ExitTradeDirection, adj.amount, op.GetNewOrderId());
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
                    Logger.Info($"Unable to liquidate operation {op} as because amount remaining is too low");
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
                        if (!symData.Symbol.IsBorrowAllowed)
                            adj.amount = freeAmount;
                        if (adj.amount > 0)
                            adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(adj.amount, adj.price);
                    }
                }
            }
            return adj;
        }

    }
}
