using LiteDB;
using Serilog;
using Serilog.Core;
using SharpTrader.BrokersApi.Binance;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class CountDownStopwatch
    {
        public TradingAlgo Algo { get; }
        public DateTime EndTime { get; private set; }
        public CountDownStopwatch(TradingAlgo algo, TimeSpan duration)
        {
            Algo = algo;
            EndTime = Algo.Time + duration;
        }
        public CountDownStopwatch(TradingAlgo algo, DateTime endTime)
        {
            Algo = algo;
            EndTime = endTime;
        }
        public bool IsElapsed => Algo.Time >= EndTime;
        public bool IsRunning => Algo.Time < EndTime;
    }

    public class MarketMakerOperationManager2 : OperationManager
    {
        Serilog.ILogger Logger;
        public TimeSpan DelayAfterOrderClosed = TimeSpan.FromSeconds(4);
        public TimeSpan DelayAfterCloseFailed = TimeSpan.FromSeconds(60);
        public TimeSpan CloseQueueTime = TimeSpan.FromMinutes(2);
        public decimal MinimumPriceChangeEntry { get; set; } = 0.003m;
        public decimal MinimumPriceChangeExit { get; set; } = 0.003m;
        public decimal EntryDistantThreshold { get; private set; }
        public decimal EntryNearThreshold { get; private set; }
        public AssetAmount TotalBudget { get; set; }

        public class MyOperationData : IChangeTracking
        {
            private volatile bool _IsChanged = true;
            private IOrder currentExitOrder;
            private IOrder currentEntryOrder;

            public HashSet<string> AllEntries { get; set; } = new HashSet<string>();
            public HashSet<string> AllExits { get; set; } = new HashSet<string>();

            public IOrder CurrentEntryOrder
            {
                get => currentEntryOrder;
                set
                {
                    if (value != null)
                        AllEntries.Add(value.Id);
                    currentEntryOrder = value;
                    _IsChanged = true;
                }
            }
            public bool HasExitOrder => CurrentExitOrder != null && CurrentExitOrder.Status < OrderStatus.Cancelled;
            public IOrder CurrentExitOrder
            {
                get => currentExitOrder;
                set
                {
                    if (currentExitOrder != null && currentExitOrder.Id != value?.Id)
                    {
                        FilledAmountByOrders += currentExitOrder.Filled;
                    }
                    if (value != null)
                        AllExits.Add(value.Id);

                    currentExitOrder = value;
                    _IsChanged = true;
                }
            }
            public bool IsChanged => _IsChanged;
            [BsonIgnore]
            public CountDownStopwatch TryCloseCountdown { get; set; }
            [BsonIgnore]
            public LiquidationTask LiquidationTask { get; set; }
            [BsonIgnore]
            public CountDownStopwatch EntryOrderCountdown { get; internal set; }

            [BsonIgnore]
            public CountDownStopwatch ExitOrderCountdown { get; internal set; }
            [BsonIgnore]
            public CountDownStopwatch CloseExitCountdown { get; internal set; }
            public decimal FilledAmountByOrders { get; private set; } = 0;

            public MyOperationData()
            {

            }

            public void AcceptChanges()
            {
                _IsChanged = false;
            }

            internal bool NoActiveExit()
            {
                return CurrentExitOrder == null || CurrentExitOrder.IsClosed;
            }

        }


        public MarketMakerOperationManager2(decimal entryDistantThreshold, decimal entryNearThreshold)
        {
            EntryDistantThreshold = entryDistantThreshold;
            EntryNearThreshold = entryNearThreshold;
        }
        override public async Task UpdateOperationsState()
        {
            var operations = Algo.ActiveOperations.Where(op => op.IsActive && !op.RiskManaged).ToList();
            List<(Operation op, MyOperationData myOpData, SymbolData symData)> operationsData = operations.Select(op =>
            {
                var myOpData = GetMyOperationData(op);
                var symData = Algo.SymbolsData[op.Symbol.Key];
                return (op, myOpData, symData);
            }).OrderByDescending(x => x.op.Signal.Priority).ToList();

            if (operationsData.Count == 0)
                return;

            // check if any operation can be closed and close it
            var tasks = operationsData.Select(x =>
            {
                var (op, myOpData, symData) = x;
                return CloseIfCan(op, myOpData, symData);

            });
            await Task.WhenAll(tasks);
        }
        public override async Task Update(TimeSlice slice)
        {
            var operations = Algo.ActiveOperations.Where(op => op.IsActive && !op.RiskManaged).ToList();
            List<(Operation op, MyOperationData myOpData, SymbolData symData)> operationsData = operations.Select(op =>
                      {
                          var myOpData = GetMyOperationData(op);
                          var symData = Algo.SymbolsData[op.Symbol.Key];
                          return (op, myOpData, symData);
                      }).OrderByDescending(x => x.op.Signal.Priority).ToList();

            if (operationsData.Count == 0)
                return;

            // check if any operation can be closed and close it
            var tasks = operationsData.Select(x =>
            {
                var (op, myOpData, symData) = x;
                return CloseIfCan(op, myOpData, symData);

            });
            await Task.WhenAll(tasks);

            // try liquidate opearations that are expired ( max time reached )
            var liqTasks = operationsData.Where(x => x.op.IsActive).Select(async x =>
            {
                var (op, myOpData, symData) = x;
                var result = true;
                //if signal exit is expired 
                //      then we must exit any pending order and liquidate everything with a market order
                if (op.IsExitExpired(Algo.Time) && myOpData.LiquidationTask == null)
                    myOpData.LiquidationTask = StartLiquidationTask(op, myOpData, symData);
                if (myOpData.LiquidationTask != null)
                {
                    result = await myOpData.LiquidationTask.Poll();
                    if (Algo.BackTesting)
                    {
                        while (!result)
                            result = await myOpData.LiquidationTask.Poll();
                    }
                }

                return result; // terminated

            });
            await Task.WhenAll(liqTasks);

            // resample operations to those that are not under liquidation liquidate, nor risk managed
            operationsData = operationsData.Where(x => x.op.IsActive).ToList();

            // check if any open entry order should be closed and close it
            var minPriority = CalculateMinimumPriorityForEntry(operationsData);
            var tasks2 = operationsData.Where(x => x.myOpData.CurrentEntryOrder != null).Select(async x =>
            {
                var (op, myOpData, symData) = x;
                if (myOpData.CurrentEntryOrder.IsClosed)
                {
                    myOpData.CurrentEntryOrder = null;
                    if (!Algo.BackTesting)
                        myOpData.EntryOrderCountdown = new CountDownStopwatch(Algo, DelayAfterOrderClosed);
                    return;
                }


                var interdicted = op.Signal.Priority < minPriority;
                var entryDistant = op.Signal.Kind == SignalKind.Buy ?
                    ((decimal)symData.Feed.Bid - op.Signal.PriceEntry) / op.Signal.PriceEntry > EntryDistantThreshold :
                    (op.Signal.PriceEntry - (decimal)symData.Feed.Ask) / op.Signal.PriceEntry > EntryDistantThreshold;
                var entryExpired = op.IsEntryExpired(Algo.Time);
                bool shouldClose = false;
                if (entryDistant || entryExpired || interdicted)
                {
                    shouldClose = true;
                    Logger.Debug("{OperationId} - Cancelling entry order {OrderId}, flags {EntryOrderFlags}", op.Id, myOpData.CurrentEntryOrder.ClientId, new { entryDistant, entryExpired, interdicted });
                }
                else
                {
                    // for performance reasons check bad price and amount only here 
                    var amount = AssetAmount.Convert(op.AmountTarget, op.Symbol.Asset, symData.Symbol, op.Signal.PriceTarget);
                    var priceAdjusted = symData.Feed.GetOrderAmountAndPriceRoundedDown(amount, op.Signal.PriceEntry);
                    var badPrice = Math.Abs(myOpData.CurrentEntryOrder.Price - priceAdjusted.price) / priceAdjusted.price > MinimumPriceChangeEntry;
                    shouldClose = badPrice;
                }
                if (shouldClose)
                {
                    await CloseEntryOrder(op, myOpData);
                    if (!Algo.BackTesting)
                        myOpData.EntryOrderCountdown = new CountDownStopwatch(Algo, DelayAfterOrderClosed);
                }
            });
            await Task.WhenAll(tasks2);

            // now we can open entry orders
            var usedBudget = operationsData.Where(x => x.op.IsActive).Sum(x => CalculateUsedBudget(x.op, x.myOpData)); // todo handle different cases, we assume here that the quote asset is the budget asset
            var budgetRemaining = TotalBudget.Amount - usedBudget;
            // the operations are already sorted by priority
            // open entry orders until the budget is used
            foreach (var (op, myOpData, symData) in operationsData.Where(x => !x.op.RiskManaged && x.myOpData.LiquidationTask == null))
            {
                var used = await OpenEntryOrder(op, myOpData, symData, budgetRemaining);
                budgetRemaining -= used;
            }

            // finally close exit orders which need to be modified
            // and post exit orders
            var tasks3 = operationsData.Where(x => !x.op.RiskManaged && x.myOpData.LiquidationTask == null).Select(async x =>
                    {
                        var (op, myOpData, symData) = x;
                        await CloseExitIfNeeded(op, myOpData, symData);
                        await OpenExitOrder(op, myOpData, symData);
                    });
            await Task.WhenAll(tasks3);
        }

        decimal GetFilledAmountByExitOrders(MyOperationData myOpData)
        {
            decimal totalFilled = 0;
            foreach (var orderId in myOpData.AllExits)
            {
                var order = Algo.Market.GetOrderById(orderId);
                if (order == null)
                {
                    Logger.Warning("Unable to find order id {OrderId}", orderId);
                }
                else
                {
                    totalFilled += order.Filled;
                }
            }
            return totalFilled;
        }

        decimal GetFilledAmountByEntryOrders(MyOperationData myOpData)
        {
            decimal totalFilled = 0;
            foreach (var orderId in myOpData.AllEntries)
            {
                var order = Algo.Market.GetOrderById(orderId);
                if (order == null)
                {
                    Logger.Warning("Unable to find order id {OrderId}", orderId);
                }
                else
                {
                    totalFilled += order.Filled;
                }
            }
            return totalFilled;
        }

        // Gives the list of the operation that are allowed to have an entry order
        // by looking at the priority of the signal, maximum numbeb of entry orders and the total budget
        private double CalculateMinimumPriorityForEntry(List<(Operation op, MyOperationData myOpData, SymbolData symData)> operationsData)
        {
            if (operationsData.Count == 0)
                return 0;
            var usedBudget = operationsData.Sum(x => CalculateUsedBudget(x.op, x.myOpData)); // todo handle different cases, we assume here that the quote asset is the budget asset
            var budgetRemaining = TotalBudget.Amount - usedBudget;
            for (int i = 0; i < operationsData.Count; i++)
            {
                var (op, myOpData, symData) = operationsData[i];
                var willOpenEntry = EnumerateEntryOrderConditions(op, myOpData, symData).All(c => c);
                if (willOpenEntry)
                {
                    var used = AssetAmount.Convert(op.AmountTarget, TotalBudget.Asset, op.Symbol, target_price: op.Signal.PriceEntry);
                    budgetRemaining -= used;
                }
                if (budgetRemaining < 0)
                {
                    // allow 2 more operations to open entry orders 
                    // to avoid ping pong effect ( open close open close )
                    var index = Math.Min(i + 2, operationsData.Count - 1);
                    return operationsData[index].op.Signal.Priority;
                }
            }
            return operationsData.Last().op.Signal.Priority - 1;
        }

        private decimal CalculateUsedBudget(Operation op, MyOperationData opData)
        {
            var usedBudget = op.Symbol.QuoteAsset == TotalBudget.Asset ? op.QuoteAmountRemaining : op.AmountRemaining;
            var entryOrder = opData.CurrentEntryOrder;
            if (entryOrder != null && !entryOrder.IsClosed)
                usedBudget += AssetAmount.Convert(
                    new AssetAmount(op.Symbol.Asset, entryOrder.Amount - entryOrder.Filled), // pick only pending amount
                    TotalBudget.Asset, op.Symbol,
                    target_price: opData.CurrentEntryOrder.Price);
            return usedBudget;
        }

        private async Task CloseIfCan(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            //if signal entry is expired and we yet didn't get to enter, then we can just   
            //      queue the operation for close 
            bool isEntryExpired = op.IsEntryExpired(Algo.Time);
            bool noActiveExit = myOpData.NoActiveExit();

            //chek if the amout remaining is small ( so need to close the operation )
            bool remainingAmountSmall = true;
            if (op.AmountRemaining > 0)
            {
                var (_, amount) = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, op.Signal.PriceTarget);
                remainingAmountSmall = amount <= 0 && !myOpData.HasExitOrder;
            }

            //queue operation for close if conditions are met
            if (isEntryExpired && noActiveExit && remainingAmountSmall && myOpData.TryCloseCountdown?.IsRunning != true)
            {
                var entryClosed = await CloseEntryOrder(op, myOpData);
                var exitClosed = await CloseExitOrder(op, myOpData); // redundant but ok
                if (entryClosed && exitClosed)
                {
                    //put in close queue
                    if (op.AmountInvested == 0)
                        Logger.Verbose("{OperationId} - scheduling for close.", op.Id);
                    else if (op.AmountRemaining == 0)
                        Logger.Information("{OperationId} - scheduling for close.", op.Id);
                    else
                        Logger.Warning("{OperationId} - scheduling op for close but amount remaining > 0, amount small={RemainingAmountSmall}", op.Id, remainingAmountSmall);
                    await CloseQueueAsync(op, CloseQueueTime);
                }
                else
                {
                    // retry in 30 seconds
                    myOpData.TryCloseCountdown = new CountDownStopwatch(Algo, TimeSpan.FromSeconds(30));
                }
                return;
            }

        }

        public enum LiquidationTaskPhase
        {
            CloseOrders,
            Liquidate,
            Done
        }

        public class LiquidationTask
        {
            public TradingAlgo Algo;
            public Operation op;
            public MyOperationData myOpData;
            public SymbolData symData;
            public MarketMakerOperationManager2 Manager;
            public int CloseOrdersAttempts { get; private set; } = 0;

            public LiquidationTaskPhase Phase { get; private set; } = LiquidationTaskPhase.CloseOrders;
            public ILogger Logger => Manager.Logger;

            public int LiquidationAttempts { get; private set; } = 0;
            public DateTime DelayUntil { get; private set; } = DateTime.MinValue;

            /// <summary>
            ///  Returns true if the task is done
            /// </summary>
            /// <returns></returns>
            public async Task<bool> Poll()
            {
                // If the service is not available let's wait
                if (!Algo.Market.IsServiceAvailable || Algo.Time < DelayUntil)
                    return false;
                switch (Phase)
                {
                    case LiquidationTaskPhase.CloseOrders:
                        await CloseOrders();
                        break;
                    case LiquidationTaskPhase.Liquidate:
                        await Liquidate();
                        break;
                    case LiquidationTaskPhase.Done:
                        await Manager.CloseQueueAsync(op, Manager.CloseQueueTime);
                        break;
                }
                return Phase == LiquidationTaskPhase.Done;
            }

            async Task CloseOrders()
            {
                var orders_closed =
                    await Manager.CloseEntryOrder(op, myOpData) &&
                    await Manager.CloseExitOrder(op, myOpData);
                CloseOrdersAttempts++;
                if (CloseOrdersAttempts > 4)
                    Logger.Warning("{OperationId} - Unable to close orders before liquidation.", op.Id);
                if (orders_closed || CloseOrdersAttempts > 4)
                    Phase = LiquidationTaskPhase.Liquidate;
                else
                    DelayUntil = Algo.Time + Manager.DelayAfterCloseFailed;
            }

            // Returns true if this phase was completed ( done or failed for too many attempts ) 
            async Task Liquidate()
            {
                var done = false;
                //immediatly liquidate everything with a market order 
                var liquidationResult = await Algo.TryLiquidateOperation(op, " operation max duration reached.");
                if (liquidationResult.order != null)
                {
                    myOpData.CurrentExitOrder = liquidationResult.order;
                    done = true;
                }
                else if (liquidationResult.amountRemainingLow)
                {
                    Logger.Information("{OperationId} - Queue operation for close because liquidation retunrned amountRemainingLow", op.Id);
                    done = true;
                }
                else
                {
                    LiquidationAttempts++;
                    if (LiquidationAttempts < 20)
                    {
                        Logger.Information("{OperationId} - Liquidation tries {LiquidationTries} ", op.Id, LiquidationAttempts);
                        DelayUntil = Algo.Time.AddSeconds(LiquidationAttempts < 4 ? 30 : 120);
                    }
                    else
                    {
                        Logger.Information("{OperationId} - Queue operation for close because LiquidationTries limit was reached.", op.Id);
                        done = true;
                    }
                }
                if (done)
                    Phase = LiquidationTaskPhase.Done;
            }
        }

        private LiquidationTask StartLiquidationTask(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            return new LiquidationTask()
            {
                Algo = Algo,
                op = op,
                myOpData = myOpData,
                symData = symData,
                Manager = this,
            };
        }

        public override Task CancelAllOrders(Operation op)
        {
            var myOpData = GetMyOperationData(op);
            var tasks = new[] {
                CloseEntryOrder(op, myOpData),
                CloseExitOrder(op, myOpData)
            };
            return Task.WhenAll(tasks);
        }

        protected override Task OnInitialize()
        {
            Logger = Algo.Logger.ForContext<MarketMakerOperationManager2>();
            return Task.CompletedTask;
        }

        public override decimal GetInvestedOrLockedAmount(ISymbolInfo symbol, string asset)
        {
            decimal total = 0;
            foreach (var op in Algo.SymbolsData[symbol.Key].ActiveOperations)
            {
                var myOpData = GetMyOperationData(op);
                if (op.Symbol.Asset == asset)
                {
                    total += op.AmountRemaining;
                    if (myOpData.CurrentEntryOrder != null)
                        total += myOpData.CurrentEntryOrder.Amount - myOpData.CurrentEntryOrder.Filled;
                }
                else if (op.Symbol.QuoteAsset == asset)
                {
                    total += op.QuoteAmountRemaining;
                    if (myOpData.CurrentEntryOrder != null)
                        total +=
                            (myOpData.CurrentEntryOrder.Amount - myOpData.CurrentEntryOrder.Filled)
                            * myOpData.CurrentEntryOrder.Price;
                }
                else
                    throw new NotSupportedException("Only supported operations where asset or quoteAsset coincide with budget asset");
            }
            return total;
        }

        private MyOperationData GetMyOperationData(Operation op)
        {
            var myOpData = op.ExecutorData as MyOperationData;
            if (myOpData == null)
            {
                myOpData = new MyOperationData();
                op.ExecutorData = myOpData;
            }

            return myOpData;
        }

        private async Task OpenExitOrder(Operation op, MyOperationData myOpData, SymbolData symData)
        {

            IEnumerable<bool> getConditions()
            {
                yield return op.IsActive;
                yield return op.AmountRemaining > 0;
                yield return myOpData.LiquidationTask == null;
                yield return !op.IsExitExpired(Algo.Time);
                yield return myOpData.CurrentExitOrder == null;
                yield return myOpData.ExitOrderCountdown?.IsRunning != true;
            };
            //---------- manage exit orders -------------- 
            if (getConditions().All(c => c))
            {
                // clip the target price based on current price
                var price = op.ExitTradeDirection == TradeDirection.Buy ?
                    Math.Min(op.Signal.PriceTarget, (decimal)symData.Feed.Ask) :
                    Math.Max(op.Signal.PriceTarget, (decimal)symData.Feed.Bid);
                // try to also use the information from orders updates to avoid double spending
                var amountRemainingReal = Math.Min(op.AmountRemaining, op.AmountInvested - GetFilledAmountByExitOrders(myOpData));
                var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(amountRemainingReal, price);
                //create a limit order 
                if (adj.amount > 0)
                {
                    adj = Algo.ClampOrderAmount(symData, op.ExitTradeDirection, adj);
                    if (adj.amount > 0)
                    {
                        //Debug.Assert(adj.amount == op.AmountRemaining);

                        var orderInfo = new OrderInfo()
                        {
                            Symbol = op.Symbol.Key,
                            Type = OrderType.Limit,
                            Effect = Algo.DoMarginTrading ? MarginOrderEffect.ClosePosition : MarginOrderEffect.None,
                            Amount = adj.amount,
                            Price = adj.price,
                            ClientOrderId = op.GetNewOrderId(),
                            Direction = op.ExitTradeDirection
                        };
                        Logger.Information("{OperationId} - Setting exit order: {ClientOrderId} {OrderDirection} {Symbol} {OrderAmount}@{OrderPrice}",
                                      op.Id, orderInfo.ClientOrderId, orderInfo.Direction, op.Symbol, orderInfo.Amount, orderInfo.Price);
                        var request = await Algo.Market.PostNewOrder(orderInfo);

                        if (request.IsSuccessful)
                        {
                            myOpData.CurrentExitOrder = request.Result;
                        }
                        else
                        {
                            //order failed, retry in 10 seconds
                            Logger.Error("{OperationId} - Failed setting exit order, reason: {Reason}", op.Id, request.ErrorInfo);
                            myOpData.ExitOrderCountdown = new CountDownStopwatch(Algo, TimeSpan.FromSeconds(10));
                        }
                    }
                }
            }

        }

        private async Task CloseExitIfNeeded(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            if (myOpData.CurrentExitOrder == null)
                return;

            if (myOpData.CurrentExitOrder.IsClosed && myOpData.CurrentExitOrder.Filled == 0)
            {
                // order was closed for some reason but not filled
                // it is safe to open an other order right away
                myOpData.CurrentExitOrder = null;
            }
            else if (myOpData.CurrentExitOrder.IsClosed)
            {
                // order was closed and filled, before opening a new one 
                // we need to make sure that the trades are registered
                myOpData.CurrentExitOrder = null;
                myOpData.ExitOrderCountdown = new CountDownStopwatch(Algo, DelayAfterOrderClosed);
            }
            else
            {
                //check if we need to change order in case that the amount invested was increased 
                var amountInOrder = myOpData.CurrentExitOrder.Amount - myOpData.CurrentExitOrder.Filled;
                // tradable amount if we close the current order
                var (tradablePrice, tradableAmount) =
                    Algo.ClampOrderAmount(symData, op.ExitTradeDirection, (op.Signal.PriceTarget, op.AmountRemaining), amountInOrder); //free to trade
                // we want to trade amount remaining as max 
                var amountToTrade = Math.Min(op.AmountRemaining, tradableAmount);
                //check if amount is wrong
                var wrongAmout = Math.Abs(amountToTrade - amountInOrder) > amountToTrade * 0.10m;
                //check if order price is wrong
                var wrongPrice = Math.Abs(myOpData.CurrentExitOrder.Price - tradablePrice) / tradablePrice > MinimumPriceChangeExit;
                var opExpired = Algo.Time > op.Signal.ExpireDate;
                if (wrongPrice || wrongAmout || opExpired)
                {
                    Logger.Debug("{OperationId} - cancelling exit order, reason: {Reason}", op.Id, new { wrongPrice, wrongAmout, opExpired });
                    var requestResult = await CloseExitOrder(op, myOpData);
                    if (requestResult)
                    {
                        myOpData.CurrentExitOrder = null;
                        // if backtesting we want to reopen exit order immediatly
                        if (!Algo.BackTesting)
                            myOpData.ExitOrderCountdown = new CountDownStopwatch(Algo, DelayAfterOrderClosed);
                    }
                    else
                    {
                        //retry  after some time
                        myOpData.CloseExitCountdown = new CountDownStopwatch(Algo, TimeSpan.FromSeconds(15));
                    }

                }
            }
        }


        IEnumerable<bool> EnumerateEntryOrderConditions(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            yield return op.IsActive;
            yield return !op.IsEntryExpired(Algo.Time);
            yield return myOpData.CurrentEntryOrder == null;
            yield return !Algo.EntriesSuspended;
            yield return myOpData.EntryOrderCountdown?.IsRunning != true;
            yield return op.Signal.Kind == SignalKind.Buy ?
                ((decimal)symData.Feed.Bid - op.Signal.PriceEntry) / op.Signal.PriceEntry < EntryNearThreshold :
                (op.Signal.PriceEntry - (decimal)symData.Feed.Ask) / op.Signal.PriceEntry < EntryNearThreshold;
        }
        /// <summary>
        /// Returns the used budget
        /// </summary>
        private async Task<decimal> OpenEntryOrder(Operation op, MyOperationData myOpData, SymbolData symData, decimal remainingBudget)
        {
            if (remainingBudget <= 0 || !EnumerateEntryOrderConditions(op, myOpData, symData).All(c => c))
                return 0;
            //--- basic conditions are met, we can try to open an entry order
            var price = op.EntryTradeDirection == TradeDirection.Buy ?
                Math.Min(op.Signal.PriceEntry, (decimal)symData.Feed.Ask) :
                Math.Max(op.Signal.PriceEntry, (decimal)symData.Feed.Bid);
            Debug.Assert(op.AmountTarget.Asset == TotalBudget.Asset); // we assume here that the quote asset is the budget asset

            var originalAmount = AssetAmount.Convert(op.AmountTarget, op.Symbol.Asset, symData.Feed, target_price: price);
            var stillToBuy = Math.Min(originalAmount - op.AmountInvested, originalAmount - GetFilledAmountByEntryOrders(myOpData));
            var remainingBudgetConverted = AssetAmount.Convert(new AssetAmount(TotalBudget.Asset, remainingBudget), op.Symbol.Asset, symData.Feed, target_price: price);
            stillToBuy = Math.Min(stillToBuy, remainingBudgetConverted);
            if (stillToBuy / originalAmount > 0.2m)
            {
                //adjust price 
                var adjusted = symData.Feed.GetOrderAmountAndPriceRoundedDown(stillToBuy, price);
                adjusted = Algo.ClampOrderAmount(symData, op.EntryTradeDirection, adjusted);
                if (adjusted.amount / originalAmount > 0.1m)
                {
                    //Debug.Assert(op.AmountInvested == 0);
                    var orderInfo = new OrderInfo()
                    {
                        Symbol = op.Symbol.Key,
                        Type = OrderType.Limit,
                        Effect = Algo.DoMarginTrading ? MarginOrderEffect.OpenPosition : MarginOrderEffect.None,
                        Amount = adjusted.amount,
                        Price = adjusted.price,
                        ClientOrderId = op.GetNewOrderId(),
                        Direction = op.EntryTradeDirection
                    };

                    Logger.Information(
                        "{OperationId} - Setting Entry: {ClientOrderId} {OrderDirection} {Symbol} {OrderAmount}@{OrderPrice}",
                        op.Id, orderInfo.ClientOrderId, orderInfo.Direction, orderInfo.Symbol, orderInfo.Amount, orderInfo.Price);

                    var req = await Algo.Market.PostNewOrder(orderInfo);

                    if (req.IsSuccessful)
                    {
                        // register operation and return to ManageEntry
                        myOpData.CurrentEntryOrder = req.Result;
                        return AssetAmount.Convert(new AssetAmount(op.Symbol.Asset, adjusted.amount), TotalBudget.Asset, op.Symbol, target_price: adjusted.price);
                    }
                    else
                    {
                        //log error and repeat operation in 30 seconds
                        Logger.Error("{OperationId} - failed opening Entry order, reason: {Reason}", op.Id, req.ErrorInfo);
                        myOpData.EntryOrderCountdown = new CountDownStopwatch(Algo, TimeSpan.FromSeconds(30));
                    }
                }
            }
            return 0;
        }

        private async Task<bool> CloseOrder(IOrder order, Operation op)
        {
            bool ok = true;
            var logger = Logger.ForContext("Symbol", op.Symbol);
            if (!order.IsClosed)
            {
                var req = await Algo.Market.OrderCancelAsync(order.Id);
                if (!req.IsSuccessful)
                {
                    var req2 = await Algo.Market.OrderSynchAsync(order.Id);
                    logger
                        .ForContext("OriginalOrderId", order.Id)
                        .Error("{OperationId} - unable to close order {OrderId}, reason: {Reason}. Trying to synch...", op.Id, order.ClientId, req.ErrorInfo);
                    //check if order was closed already
                    if (req2.IsSuccessful)
                        order = req2.Result;
                    else
                    {
                        ok = false;
                        logger.ForContext("OriginalOrderId", order.Id)
                            .Error("{OperationId} - unable to synch order {OrderId}, reason: {Reason}", op.Id, order.ClientId, req.ErrorInfo);
                    }

                    if (!order.IsClosed)
                    {
                        ok = false;
                        logger.Error("{OperationId} - order {OrderId} still open after sync.", op.Id, order.ClientId);
                    }
                }
            }

            return ok;
        }

        private async Task<bool> CloseEntryOrder(Operation operation, MyOperationData opdata)
        {
            if (opdata.CurrentEntryOrder == null)
                return true;
            var closedok = await CloseOrder(opdata.CurrentEntryOrder, operation);
            if (closedok)
                opdata.CurrentEntryOrder = null;
            return closedok;
        }

        private async Task<bool> CloseExitOrder(Operation operation, MyOperationData opdata)
        {
            if (opdata.CurrentExitOrder == null)
                return true;
            var closedok = await CloseOrder(opdata.CurrentExitOrder, operation);
            if (closedok)
                opdata.CurrentExitOrder = null;
            return closedok;
        }

        public override Task CancelEntryOrders()
        {
            List<Task> tasks = new List<Task>();
            foreach (var op in Algo.ActiveOperations)
            {
                var myData = GetMyOperationData(op);
                tasks.Add(CloseEntryOrder(op, myData));
            }
            return Task.WhenAll(tasks);
        }

        private async Task CloseQueueAsync(Operation op, TimeSpan delay)
        {
            //first the operation enters a queue where it will be monitored for some time, then it will be closed
            //if a new trade arrives the operation will be resumed
            var myOpData = GetMyOperationData(op);
            Debug.Assert(myOpData.CurrentEntryOrder == null);
            Debug.Assert(myOpData.CurrentExitOrder == null || myOpData.CurrentExitOrder.IsClosed);

            //close current entry order
            await CloseEntryOrder(op, myOpData);

            //close current exit order
            await CloseExitOrder(op, myOpData);

            op.ScheduleClose(Algo.Time + delay);
        }
    }
}
