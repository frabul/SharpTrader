using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    /// Executes market orders to open and close operation
    /// </summary>
    public class ImmediateExecutionOperationManager : OperationManager
    {
        public override Task CancelAllOrders(Operation op)
        {
            return Task.CompletedTask;
        }

        public override Task CancelEntryOrders()
        {
            return Task.CompletedTask;
        }

        public override void OnSymbolsChanged(SelectedSymbolsChanges changes)
        {

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
            }
            return execData;
        }

        private async Task ManageSymbol(SymbolData symData, TimeSlice slice)
        {
            var symbol = symData.Feed.Symbol;
            //foreach operation check if the entry is enough
            foreach (Operation operation in symData.ActiveOperations)
            {
                MyOperationData execData = InitializeOperationData(operation);

                //check if it needs to be closed
                if (operation.IsEntryExpired(Algo.Time) && (operation.AmountInvested <= 0 || operation.AmountRemaining / operation.AmountInvested <= 0.03m))
                {
                    operation.ScheduleClose(Algo.Time.AddSeconds(5));
                    continue;
                }

                //check if entry needed
                if (operation.AmountInvested <= 0 && !operation.IsEntryExpired(Algo.Time) && !Algo.EntriesSuspended)
                {
                    var gotEntry = operation.Type == OperationType.BuyThenSell ?
                                     (decimal)symData.Feed.Ask <= operation.Signal.PriceEntry :
                                     (decimal)symData.Feed.Bid >= operation.Signal.PriceEntry;
                    //todo  check if the last entry order was effectively executed
                    //check if we have already sent an order
                    if (execData.LastEntryOrder == null)
                    {
                        var originalAmount = AssetAmount.Convert(operation.AmountTarget, operation.Symbol.Asset, symData.Feed);
                        var stillToBuy = originalAmount - operation.AmountInvested;
                        if (stillToBuy / originalAmount > 0.2m)
                        {
                            //TODO check requirements for order: price precision, minimum amount, min notional, available money
                            var orderInfo = new OrderInfo()
                            {
                                Symbol = symbol.Key,
                                Type = OrderType.Market,
                                Effect = Algo.DoMarginTrading ? MarginOrderEffect.OpenPosition : MarginOrderEffect.None,
                                Amount = stillToBuy,
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
                                //todo log error
                            }
                        }
                    }
                }

                //check if exit needed
                if (execData.LastExitOrder == null)
                {
                    var shouldExit = operation.Signal.ExpireDate < Algo.Time;
                    var gotTarget = operation.Type == OperationType.BuyThenSell ?
                                        (decimal)symData.Feed.Bid >= operation.Signal.PriceTarget :
                                        (decimal)symData.Feed.Ask <= operation.Signal.PriceTarget;

                    if (shouldExit || gotTarget)
                    {
                        var amount = operation.AmountRemaining;
                        //TODO check requirements for order: price precision, minimum amount, min notional, available money
                        var orderInfo = new OrderInfo()
                        {
                            Symbol = symbol.Key,
                            Type = OrderType.Market,
                            Effect = Algo.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                            Amount = amount,
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
                            //TODO log error
                        }
                    }
                }
            }
        }


        class MyOperationData
        {
            public IOrder LastEntryOrder;
            public IOrder LastExitOrder;
            public TradeDirection EntryDirection;
            public TradeDirection ExitDirection;
        }
    }
}