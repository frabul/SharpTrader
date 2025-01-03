using LiteDB;
using Serilog;
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
        public DateTime EndTime { get; private set; }
        public CountDownStopwatch(TimeSpan duration)
        {
            EndTime = DateTime.UtcNow + duration;
        }
        public CountDownStopwatch(DateTime endTime)
        {
            EndTime = endTime;
        }
        public bool IsElapsed => DateTime.UtcNow >= EndTime;
    }

    public class MarketMakerOperationManager2 : OperationManager
    {
        // This manager must execute 3 tasks for each opeartion:
        // 1. Accumuation task (manages entry orders)
        // 2. Distribution task (manages exit orders)
        // 3. Super task (manages the whole operation state)
        //
        // Accumulation task tries to accumulate the asset with limit orders at the target price
        // It opens a limit order when the price is near the target price and cancels the order when the price is far from the target price.
        // It stops accumulating if the target amount is reached or if the signal expires.
        //
        // Distribution task tries to distribute the asset with limit orders at the target price
        // It checks that there is an active exit order. Modifies the exit order if needed ( price or amount changed ).
        // Here it is important to avoid double spending, it happens if the order results closed but the trades are not registered yet.
        //
        // Super task manages the operation lifetime and state. It checks if the operation is closing or closed for any reason and updates its state for 
        // the external observers. It also checks if the operation is expired and queues it for close.
        //
        // It should be able to manage multiple operations with different
        Serilog.ILogger Logger;
        public TimeSpan DelayAfterOrderClosed = TimeSpan.FromSeconds(15);
        public TimeSpan DelayAfterCloseFailed = TimeSpan.FromSeconds(60);
        public TimeSpan CloseQueueTime = TimeSpan.FromMinutes(2);
        public decimal MinimumPriceChangeEntry { get; set; } = 0.003m;
        public decimal MinimumPriceChangeExit { get; set; } = 0.003m;
        public uint MaximumPendingEntryOrdersCount { get; set; } = 1000;
        public decimal EntryDistantThreshold { get; private set; }
        public decimal EntryNearThreshold { get; private set; }
        public AssetAmount TotalBudget { get; set; }

        private int CurrentlyOpenEntryOrdersCount { get; set; }


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
                    if (value != null)
                        AllExits.Add(value.Id);
                    currentExitOrder = value;
                    _IsChanged = true;
                }
            }
            public bool IsChanged => _IsChanged;
            public CountDownStopwatch TryCloseCountdown { get; set; } = new CountDownStopwatch(TimeSpan.Zero);
            public bool Liquidation { get; set; } = false;

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

        public override async Task Update(TimeSlice slice)
        {
            var openEntryOrdersCont = Algo.ActiveOperations.Count(op =>
            {
                var myOpData = GetMyOperationData(op);
                return myOpData.CurrentEntryOrder != null && myOpData.CurrentEntryOrder.Status < OrderStatus.Cancelled;
            });

            var operations = Algo.ActiveOperations.Where(op => op.IsActive && !op.RiskManaged).ToList();
            List<(Operation op, MyOperationData myOpData, SymbolData symData)> operationsData = operations.Select(op =>
                      {
                          var myOpData = GetMyOperationData(op);
                          var symData = Algo.SymbolsData[op.Symbol.Key];
                          return (op, myOpData, symData);
                      }).ToList();
            // check if any operation can be closed and close it
            var tasks = operationsData.Select(x =>
            {
                var (op, myOpData, symData) = x;
                return CloseIfCan(op, myOpData, symData);
            });
            await Task.WhenAll(tasks);
            // try liquidate opearations that should be liquidated

            // check if any open entry order should be closed and close it


        }

        private async Task CloseIfCan(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            //if operation is closing or closed we terminate the task chain
            if (op.IsClosing || op.IsClosed)
            {
                if (op.AmountRemaining > 0)
                    this.Logger.Warning("{OperationId} - Closing operation but amountremaining is > 0", op);
                return;
            }

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
            if (isEntryExpired && noActiveExit && remainingAmountSmall && myOpData.TryCloseCountdown.IsElapsed)
            {
                var entryClosed = await CloseEntryOrder(op, myOpData);
                var exitClosed = await CloseExitOrder(op, myOpData); // redundant but ok
                if (entryClosed && exitClosed)
                {
                    var conditions = new { isEntryExpired, noActiveExit, remainingAmountSmall };
                    //put in close queue
                    if (op.AmountInvested == 0)
                        Logger.Verbose("{OperationId} - scheduling for close.", op.Id);
                    else if (op.AmountRemaining == 0)
                        Logger.Information("{OperationId} - scheduling for close.", op.Id);
                    else
                        Logger.Warning("{OperationId} - scheduling op for close but amount remaining > 0, {RemainingAmountSmall}", op.Id, remainingAmountSmall);
                    await CloseQueueAsync(op, CloseQueueTime);
                }
                else
                {
                    // retry in 30 seconds
                    myOpData.TryCloseCountdown = new CountDownStopwatch(TimeSpan.FromSeconds(30));
                }
                return;
            }

            //if signal exit is expired 
            //      then we must exit any pending order and liquidate everything with a market order
            if (op.IsExitExpired(Algo.Time))
            {
                var entryClosed = await CloseEntryOrder(op, myOpData);
                var exitClosed = await CloseExitOrder(op, myOpData);

                myOpData.Liquidation = true;

                //set next step to close orders and liquidate operation
                self.Next = new DeferredTaskDelegate(CloseOrdersAndLiquidate);
                self.LiquidateReason = " exit deadtime elapsed.";
                //also call next step immediatly only during backtesting
                if (Algo.BackTesting)
                    return await self.Next.Invoke(self);
            }

        }

        private async Task LiquidateOperation(Operation op, MyOperationData myOpData, SymbolData symData)
        {
            myOpData.Liquidation = true;
            // close pending orders
            uint tries = 0;
            do
            {
                if (Algo.Market.IsServiceAvailable)
                {
                    await CloseEntryOrder(op, myOpData);
                    await CloseExitOrder(op, myOpData);
                    tries++;
                    if (tries > 4)
                        Logger.Warning("{OperationId} - Unable to close orders before liquidation.", op.Id);
                }
                await Task.Delay(20000); // always wait 20 seconds after closing orders so that the trades are registered
            } while (myOpData.CurrentEntryOrder != null || myOpData.CurrentEntryOrder != null);

            //liquidate operation
            tries = 0;
            bool terminate = false;
            while (op.AmountRemaining > 0 || terminate)
            {
                //immediatly liquidate everything with a market order 
                //Logger.Information("Try liquidate operation with market order because {Reason}", self.LiquidateReason);
                if (!Algo.Market.IsServiceAvailable)
                {
                    await Task.Delay(20000);
                    continue;
                }

                var liquidationResult = await Algo.TryLiquidateOperation(op, " operation max duration reached.");
                if (liquidationResult.order != null)
                {
                    myOpData.CurrentExitOrder = liquidationResult.order;
                    terminate = true;
                }
                else if (liquidationResult.amountRemainingLow)
                {
                    Logger.Information("{OperationId} - Queue operation for close because liquidation retunrned amountRemainingLow", op.Id);
                    terminate = true;
                }
                else
                {
                    tries++;
                    if (tries < 20)
                    {
                        Logger.Information("{OperationId} - Liquidation tries {LiquidationTries} ", op.Id, tries);
                        await Task.Delay(TimeSpan.FromMinutes(2));
                    }
                    else
                    {
                        Logger.Information("{OperationId} - Queue operation for close because LiquidationTries limit was reached.", op.Id);
                        terminate = true;
                    }
                }

            }


            await CloseQueueAsync(op, CloseQueueTime);

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

            InitOpTasks(op, myOpData);
            return myOpData;
        }


        private async Task<bool> CloseOrdersAndLiquidate(DeferredTask self)
        {
            //se questo expire accade in contemporanea con un trade generato da exit order abbiamo un problema di corsa critica...
            //     quindi per prima cosa cancelliamo ordine, poi controlliamo il filled di tutti gli ordini, dopo 1 minuto liquidiamo
            var entryClosed = await CloseEntryOrder(self.Op, self.myOpData);
            var exitClosed = await CloseExitOrder(self.Op, self.myOpData);
            if (entryClosed && exitClosed)
            {
                //schedule the liquidation for later 
                self.LiquidationTries = 0;
                self.Next = LiquidateOperation;
                self.Time = Algo.Time + DelayAfterOrderClosed;
            }
            else
            {
                //retry in 30 seconds
                self.Time = Algo.Time + DelayAfterCloseFailed;
            }
            return false;
        }





        private async Task<bool> OpenExitOrder(DeferredTask self)
        {
            var myOpData = self.myOpData;
            Operation op = self.Op;
            SymbolData symData = self.SymbolData;
            Debug.Assert(myOpData.CurrentExitOrder == null || !Algo.BackTesting, "Current exit order is not null");

            if (op.IsClosing || op.IsClosed)
            {
                if (op.AmountRemaining > 0)
                    Logger.Warning("{OperationId} - Operation was closed but amountremaining is > 0", op.Id);
                return true;
            }

            //---------- manage exit orders -------------- 
            if (myOpData.CurrentExitOrder == null)
            {
                // clip the target price based on current price
                var price = op.ExitTradeDirection == TradeDirection.Buy ?
                    Math.Min(op.Signal.PriceTarget, (decimal)self.SymbolData.Feed.Ask) :
                    Math.Max(op.Signal.PriceTarget, (decimal)self.SymbolData.Feed.Bid);

                if (op.AmountRemaining > 0 && !op.IsExitExpired(Algo.Time))
                {
                    //if we have no order 
                    if (myOpData.CurrentExitOrder == null)
                    {
                        var adj = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, price);
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
                                    self.Next = MonitorExit;
                                }
                                else
                                {
                                    //order failed, retry in 10 seconds
                                    Logger.Error("{OperationId} - Failed setting exit order, reason: {Reason}", op.Id, request.ErrorInfo);
                                    self.Time = Algo.Time.AddSeconds(10);
                                }
                            }
                        }
                    }
                }
            }
            else
                self.Next = MonitorExit;
            return false;
        }

        private async Task<bool> MonitorExit(DeferredTask self)
        {
            var myOpData = self.myOpData;
            Operation op = self.Op;
            SymbolData symData = self.SymbolData;
            //if the operation is closing or is already closed then we can stop monitoring exit
            if (op.IsClosing || op.IsClosed)
                return true;

            Debug.Assert(myOpData.CurrentExitOrder != null);
            if (myOpData.CurrentExitOrder != null && !myOpData.CurrentExitOrder.IsClosed)
            {
                //check if we need to change order in case that the amount invested was increased 
                var amountInOrder = myOpData.CurrentExitOrder.Amount - myOpData.CurrentExitOrder.Filled;
                var availableForTrading =
                    Algo.ClampOrderAmount(symData, op.ExitTradeDirection, (op.Signal.PriceTarget, op.AmountRemaining)).amount //free to trade
                                            + amountInOrder;                      //amount derived from cancelling the order
                // we want to trade amount remaining as max 
                var amountToTrade = Math.Min(op.AmountRemaining, availableForTrading);
                //check if amount is wrong
                var wrongAmout = Math.Abs(amountToTrade - amountInOrder) > amountToTrade * 0.10m;
                //check if order price is wrong
                var wrongPrice = Math.Abs(myOpData.CurrentExitOrder.Price - op.Signal.PriceTarget) / op.Signal.PriceTarget > MinimumPriceChangeExit;
                var opExpired = Algo.Time > op.Signal.ExpireDate;
                if (wrongPrice || wrongAmout || opExpired)
                {
                    Logger.Debug("{OperationId} - cancelling exit order, reason: {Reason}", op.Id, new { wrongPrice, wrongAmout, opExpired });
                    var requestResult = await this.CloseExitOrder(op, myOpData);
                    if (requestResult)
                    {
                        myOpData.CurrentExitOrder = null;
                        self.Next = OpenExitOrder;
                        // if backtesting we want to reopen exit order immediatly
                        if (Algo.BackTesting)
                            await self.Next(self);
                        else
                            self.Time = Algo.Time + DelayAfterOrderClosed;

                        //in case signal expired there is no pressure to open a new order, take some more time ( to avoid double exit )
                        if (Algo.Time > op.Signal.ExpireDate)
                            self.Time = Algo.Time.AddSeconds(30);
                    }
                    else
                    {
                        //retry in 20 seconds
                        self.Time = Algo.Time + TimeSpan.FromSeconds(19);
                    }

                }
            }
            else
            {
                myOpData.CurrentExitOrder = null;
            }

            if (myOpData.CurrentExitOrder == null)
                self.Next = OpenExitOrder;

            return false;
        }

        private async Task<bool> OpenEntryOrder(DeferredTask self)
        {
            var myOpData = self.myOpData;
            Operation op = self.Op;
            SymbolData symData = self.SymbolData;

            Debug.Assert(myOpData.CurrentEntryOrder == null || !Algo.BackTesting);

            if (op.IsClosing || op.IsClosed)
            {
                if (op.AmountRemaining > 0)
                    Logger.Warning("{OperationId} - Operation is closing but amountremaining is > 0", op.Id);
                return true;
            }
            // check the number of open entry orders
            if (this.CurrentlyOpenEntryOrdersCount >= this.MaximumPendingEntryOrdersCount)
                return false;
            if (!op.IsEntryExpired(Algo.Time) && myOpData.CurrentEntryOrder == null)
            {
                //--- open a new entry if needed ---
                var entryNear = op.Signal.Kind == SignalKind.Buy ?
                    ((decimal)symData.Feed.Bid - op.Signal.PriceEntry) / op.Signal.PriceEntry < EntryNearThreshold :
                    (op.Signal.PriceEntry - (decimal)symData.Feed.Ask) / op.Signal.PriceEntry < EntryNearThreshold;

                if (entryNear && !Algo.EntriesSuspended)
                {
                    var price = op.EntryTradeDirection == TradeDirection.Buy ?
                            Math.Min(op.Signal.PriceEntry, (decimal)self.SymbolData.Feed.Ask) :
                            Math.Max(op.Signal.PriceEntry, (decimal)self.SymbolData.Feed.Bid);
                    var originalAmount = AssetAmount.Convert(op.AmountTarget, op.Symbol.Asset, symData.Feed, target_price: price);
                    var stillToBuy = originalAmount - op.AmountInvested;
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
                                this.CurrentlyOpenEntryOrdersCount++;
                                // register operation and return to ManageEntry
                                myOpData.CurrentEntryOrder = req.Result;
                                self.Next = MonitorEntry;
                            }
                            else
                            {
                                //log error and repeat operation in 30 seconds
                                Logger.Error("{OperationId} - failed opening Entry order, reason: {Reason}", op.Id, req.ErrorInfo);
                                self.Time = Algo.Time.AddSeconds(30);
                            }
                        }
                    }
                }
            }
            return false;
        }

        private async Task<bool> MonitorEntry(DeferredTask self)
        {
            var myOpData = self.myOpData;
            Operation op = self.Op;
            SymbolData symData = self.SymbolData;

            if (op.IsClosing || op.IsClosed)
                return false;

            //----------------------- manage entry orders -------------------------------- 
            //  open entry if we got near the target price
            //  cancel entry if we are too far  
            var entryDistant = op.Signal.Kind == SignalKind.Buy ?
              ((decimal)symData.Feed.Bid - op.Signal.PriceEntry) / op.Signal.PriceEntry > EntryDistantThreshold :
              (op.Signal.PriceEntry - (decimal)symData.Feed.Ask) / op.Signal.PriceEntry > EntryDistantThreshold;

            if (myOpData.CurrentEntryOrder != null)
            {
                //var badAmout = Math.Abs(LastEntryOrder.Amount - orderAmount) / orderAmount > 0.20m; 
                var amount = AssetAmount.Convert(op.AmountTarget, op.Symbol.Asset, symData.Feed);
                var priceAdjusted = symData.Feed.GetOrderAmountAndPriceRoundedDown(amount, op.Signal.PriceEntry);
                var badPrice = Math.Abs(myOpData.CurrentEntryOrder.Price - priceAdjusted.price) / priceAdjusted.price > MinimumPriceChangeEntry;
                var entryExpired = op.IsEntryExpired(Algo.Time);

                //N.B. also when signal entry is not valid we keep monitoring as it could be updated
                if (entryDistant || badPrice || entryExpired)
                {
                    Logger.Debug("{OperationId} - Cancelling entry order {OrderId}, flags {EntryOrderFlags}", op.Id, myOpData.CurrentEntryOrder.ClientId, new { entryDistant, badPrice, entryExpired });
                    var orderClosed = await CloseEntryOrder(op, myOpData);
                    if (orderClosed)
                    {
                        //then we schedule a task to open new ordder
                        self.Time = Algo.Time + DelayAfterOrderClosed;
                        self.Next = new DeferredTaskDelegate(OpenEntryOrder);
                        //if we are backtesting, next tick will be much later so we call continuation instantly
                        if (Algo.BackTesting)
                            await self.Next(self);
                    }
                }
            }

            return false;
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
                    logger.Error("{OperationId} - unable to close order {OrderId}, reason: {Reason}. Trying to synch...", op.Id, order.ClientId, req.ErrorInfo);
                    //check if order was closed already
                    var req2 = await Algo.Market.OrderSynchAsync(order.Id);
                    if (req2.IsSuccessful)
                        order = req2.Result;
                    else
                    {
                        ok = false;
                        logger.Error("{OperationId} - unable to synch order {OrderId}, reason: {Reason}", op.Id, order.ClientId, req.ErrorInfo);
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
