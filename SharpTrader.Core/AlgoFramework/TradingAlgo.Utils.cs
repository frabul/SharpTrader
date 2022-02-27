using System;
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

        public class AlgoResultsSlice
        {
            public string AlgoName { get; set; }
            public DateTime PeriodStart { get; set; }
            public DateTime PeriodEnd { get; set; }
            public decimal Volume { get; set; }
            public decimal Equity { get; set; }
            public decimal GainsRealized { get; set; }
            public decimal GainsPartial { get; set; }
            public int OperationsCount { get; set; }
            public string BaseAsset { get; set; }
        }

        public AlgoResultsSlice GetTradingResults(DateTime startTime, DateTime endTime, string baseAsset)
        {
            var allOperations = QueryOperations(op =>  op["CreationTime"].AsDateTime >= startTime && op["CreationTime"].AsDateTime < endTime && op["AmountInvested"].AsDecimal > 0)
                                        .Select(op => this.OperationFromBson(op)).ToList();
            var periodResults = new AlgoResultsSlice()
            {
                AlgoName = this.Name,
                PeriodStart = startTime,
                PeriodEnd = endTime,
                BaseAsset = baseAsset
            };
            foreach (var op in allOperations)
            {
                //check that the operation is not anomalous
                var gain = op.CalculateGainAsQuteAsset(0.00075m);
                var roi = gain / op.QuoteAmountInvested;
                if (roi > 0.1m || op.AmountRemaining < 0)
                {
                    Logger.Warning("Found operation {OperationId} with anomalous gain {gain} or amount remaining {OperationAmountRemaining} ", op.Id, gain, op.AmountRemaining);
                    continue;
                }
                //-------
                periodResults.OperationsCount += 1;
                if ((op.IsClosed || op.IsClosing) && op.AmountRemaining / op.AmountInvested <= 0.05m)
                {
                    var spent = op.Entries.Sum(e => e.Price * e.Amount);
                    var recovered = op.Exits.Sum(e => e.Price * e.Amount);
                    periodResults.Volume += spent + recovered;
                    periodResults.Equity += roi;
                    periodResults.GainsRealized += gain;
                    periodResults.GainsPartial += gain;
                }
                else if ((op.AmountRemaining / op.AmountInvested) > 0.02m)
                {
                    //get the current price for the symbol
                    if (this.SymbolsData.TryGetValue(op.Symbol.Key, out SymbolData symData))
                    {
                        if (symData.Feed != null)
                        {
                            var curPrice = symData.Feed.Bid;
                            var spent = op.AmountInvested * op.AverageEntryPrice;
                            var recovered = op.AmountLiquidated * op.AverageExitPrice;
                            var couldRecover = op.AmountRemaining * (decimal)curPrice;
                            periodResults.Volume += spent + recovered;
                            periodResults.Equity += (recovered + couldRecover - spent) / op.QuoteAmountInvested;
                            periodResults.GainsPartial += (recovered + couldRecover - spent) * (1 - 0.00075m * 2);
                        }
                    }
                }
            }
            return periodResults;
        }
    }
}
