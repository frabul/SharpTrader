using LiteDB;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    /// Executes market orders to open and close operation
    /// </summary>
    public class ImmediateExecutionOperationManager : OperationManager
    {
        Logger Logger = LogManager.GetCurrentClassLogger();
        public override Task CancelAllOrders(Operation op)
        {
            return Task.CompletedTask;
        }

        public override Task CancelEntryOrders()
        {
            return Task.CompletedTask;
        }


        public override Task Update(TimeSlice slice)
        {
            List<Task> tasks = new List<Task>();
            foreach (var symData in Algo.SymbolsData.Values)
            {
                tasks.Add(ManageSymbol(symData, slice));
            }
            return Task.WhenAll(tasks);
        }
        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override decimal GetInvestedOrLockedAmount(ISymbolInfo symbol, string asset)
        {
            return 0m;
        }

        private MyOperationData InitializeOperationData(Operation op)
        {
            var execData = op.ExecutorData as MyOperationData;
            if (execData == null)
            {
                execData = new MyOperationData();
                if (op.Type == OperationType.BuyThenSell)
                {
                    execData.EntryDirection = TradeDirection.Buy;
                    execData.ExitDirection = TradeDirection.Sell;
                }
                else if (op.Type == OperationType.SellThenBuy)
                {
                    execData.EntryDirection = TradeDirection.Sell;
                    execData.ExitDirection = TradeDirection.Buy;
                }
                else
                    throw new NotSupportedException("Only supports buyThenSell and SellThenBuy operations");
                op.ExecutorData = execData;
            }
            return execData;
        }

        private async Task ManageSymbol(SymbolData symData, TimeSlice slice)
        {
            var symbol = symData.Symbol;
            //foreach operation check if the entry is enough
            foreach (Operation operation in symData.ActiveOperations)
            {
                MyOperationData execData = InitializeOperationData(operation);

                //check if it needs to be closed
                bool entryIsExpired = operation.IsEntryExpired(Algo.Time);
                bool amountRemainingLow = (operation.AmountInvested > 0 && operation.AmountRemaining / operation.AmountInvested <= 0.03m);
                if ((entryIsExpired && operation.AmountInvested <= 0) || amountRemainingLow)
                {
                    operation.ScheduleClose(Algo.Time.AddSeconds(5));
                    continue;
                }
                if (operation.IsClosing)
                    continue;

                //check if entry needed
                if (!entryIsExpired && !Algo.EntriesSuspended)
                {
                    var gotEntry = operation.Type == OperationType.BuyThenSell ?
                                     (decimal)symData.Feed.Ask <= operation.Signal.PriceEntry :
                                     (decimal)symData.Feed.Bid >= operation.Signal.PriceEntry;
                    //todo  check if the last entry order was effectively executed
                    //check if we have already sent an order
                    if (gotEntry && execData.LastEntryOrder == null)
                    {
                        var originalAmount = AssetAmount.Convert(operation.AmountTarget, operation.Symbol.Asset, symData.Feed);
                        var stillToBuy = originalAmount - operation.AmountInvested;
                        if (stillToBuy / originalAmount > 0.2m)
                        {
                            var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(stillToBuy, (decimal)symData.Feed.Ask);
                            //TODO check requirements for order: price precision, minimum amount, min notional, available money
                            adj = Algo.ClampOrderAmount(symData, execData.EntryDirection, adj);
                            if (adj.amount > 0)
                            {

                                var orderInfo = new OrderInfo()
                                {
                                    Symbol = symbol.Key,
                                    Type = OrderType.Market,
                                    Effect = Algo.DoMarginTrading ? MarginOrderEffect.OpenPosition : MarginOrderEffect.None,
                                    Amount = adj.amount,
                                    ClientOrderId = operation.GetNewOrderId(),
                                    Direction = execData.EntryDirection
                                };
                                var ticket = await Algo.Market.PostNewOrder(orderInfo);
                                if (ticket.Status == RequestStatus.Completed)
                                {
                                    //Market order was executed - we should expect the trade on next update 
                                    execData.LastEntryOrder = ticket.Result;
                                }
                                else
                                {
                                    Logger.Error($"Error while trying to post entry order for symbol {symData.Symbol.Key}: {ticket.ErrorInfo} ");
                                }
                            }
                        }
                    }
                }

                //check if exit needed
                if (execData.LastExitOrder == null && operation.AmountRemaining > 0)
                {
                    var exitByExpiration = operation.Signal.ExpireDate < Algo.Time;
                    var gotTarget = operation.Type == OperationType.BuyThenSell ?
                                        (decimal)symData.Feed.Bid >= operation.Signal.PriceTarget :
                                        (decimal)symData.Feed.Ask <= operation.Signal.PriceTarget;

                    if (exitByExpiration || gotTarget)
                    {

                        var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(operation.AmountRemaining, (decimal)symData.Feed.Bid);
                        //TODO check requirements for order: price precision, minimum amount, min notional, available money
                        adj = Algo.ClampOrderAmount(symData, execData.ExitDirection, adj);

                        if (adj.amount > 0)
                        {
                            var orderInfo = new OrderInfo()
                            {
                                Symbol = symbol.Key,
                                Type = OrderType.Market,
                                Effect = Algo.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                                Amount = adj.amount,
                                ClientOrderId = operation.GetNewOrderId(),
                                Direction = execData.ExitDirection
                            };
                            var ticket = await Algo.Market.PostNewOrder(orderInfo);
                            if (ticket.Status == RequestStatus.Completed)
                            {
                                //Market order was executed - we should expect the trade on next update 
                                execData.LastExitOrder = ticket.Result;
                            }
                            else
                            {
                                Logger.Error($"Error while trying to post exit order for symbol {symData.Symbol.Key}: {ticket.ErrorInfo} ");
                            }
                        }


                    }
                }
            }
        }


        class MyOperationData : IChangeTracking
        {
            private IOrder lastEntryOrder;
            private IOrder lastExitOrder;
            private TradeDirection entryDirection;
            private TradeDirection exitDirection;

            public IOrder LastEntryOrder
            {
                get => lastEntryOrder;
                set
                {
                    lastEntryOrder = value;
                    IsChanged = true;
                }
            }
            public IOrder LastExitOrder
            {
                get => lastExitOrder;
                set
                {
                    lastExitOrder = value;
                    IsChanged = true;
                }
            }
            public TradeDirection EntryDirection
            {
                get => entryDirection;
                set
                {
                    entryDirection = value;
                    IsChanged = true;
                }
            }
            public TradeDirection ExitDirection
            {
                get => exitDirection;
                set
                {
                    exitDirection = value;
                    IsChanged = true;
                }
            }

            public bool IsChanged { get; private set; } = true;

            public void AcceptChanges()
            {
                IsChanged = false;
            }
        }
    }
}