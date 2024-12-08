using LiteDB;
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
    public class MarketMakerOperationManager : OperationManager
    {
        //todo after each action we should give 10 seconds delay before perfoming a new order so we get updates
        NLog.Logger Logger;
        public TimeSpan DelayAfterOrderClosed = TimeSpan.FromSeconds(15);
        public TimeSpan DelayAfterCloseFailed = TimeSpan.FromSeconds(60);
        public TimeSpan CloseQueueTime = TimeSpan.FromMinutes(2);

        [Obfuscation(Exclude = true)]
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

            [BsonIgnore] internal bool Initialized { get; set; } = false;
            [BsonIgnore] internal DeferredTask OperationManager { get; set; }
            [BsonIgnore] internal DeferredTask EntryManager { get; set; }
            [BsonIgnore] internal DeferredTask ExitManager { get; set; }

            public void AcceptChanges()
            {
                _IsChanged = false;
            }

            public MyOperationData()
            {

            }

            internal bool NoActiveExit()
            {
                return CurrentExitOrder == null || CurrentExitOrder.IsClosed;
            }
        }
        public decimal EntryDistantThreshold { get; private set; }
        public decimal EntryNearThreshold { get; private set; }
        private Random rand = new Random();
        public MarketMakerOperationManager(decimal entryDistantThreshold, decimal entryNearThreshold)
        {
            Logger = NLog.LogManager.GetLogger(nameof(MarketMakerOperationManager));
            EntryDistantThreshold = entryDistantThreshold;
            EntryNearThreshold = entryNearThreshold;
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

        public override async Task Update(TimeSlice slice)
        {

            var symbols = Algo.SymbolsData.Values.OrderBy(el => rand.NextDouble()).ToArray();
            //randomize the order of completion
            foreach (var symSlice in symbols)
            {
                //if we got a new signal let 
                try
                {
                    await ManageSymbol(slice, symSlice);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception during MarketMakerOperationManager.Update: {0}, symbol {1}", ex.Message, symSlice.Symbol.Key);
                }
            }
        }

        protected override Task OnInitialize()
        {
            if (Algo.BackTesting)
                this.rand = new Random(123);
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

        private void InitOpTasks(Operation op, MyOperationData myOpData)
        {
            if (myOpData.Initialized)
                return;
            void OnOpResumed(Operation o)
            {
                var mydata = (o.ExecutorData as MyOperationData);
                mydata.Initialized = false;
                InitOpTasks(o, mydata);
            }
            op.OnResumed -= OnOpResumed;
            op.OnResumed += OnOpResumed;

            myOpData.Initialized = true;

            var symData = Algo.SymbolsData[op.Symbol.Key];

            if (myOpData.OperationManager == null)
                myOpData.OperationManager =
                    new DeferredTask()
                    {
                        myOpData = myOpData,
                        Op = op,
                        SymbolData = symData,
                        Next = MonitorOperation,
                    };

            if (myOpData.EntryManager == null)
                myOpData.EntryManager =
                    new DeferredTask()
                    {
                        myOpData = myOpData,
                        Op = op,
                        SymbolData = symData,
                        Next = OpenEntryOrder,
                    };
            if (myOpData.ExitManager == null)
                myOpData.ExitManager =
                    new DeferredTask()
                    {
                        myOpData = myOpData,
                        Op = op,
                        SymbolData = symData,
                        Next = OpenExitOrder,
                    };
        }

        private async Task ManageSymbol(TimeSlice slice, SymbolData symData)
        {
            // for each operation check entry and exit orders
            foreach (Operation op in symData.ActiveOperations)
            {
                var myOpData = GetMyOperationData(op);

                //queue the operation for close  if
                //   entry expired and amount remaining <= 0 
                if (!op.IsClosed && !op.IsClosing && !op.RiskManaged)
                {
                    //if operation is not closed or closing we must assure that there are the tasks to manage it 
                    if (myOpData.OperationManager != null && await myOpData.OperationManager.Next(myOpData.OperationManager))
                        myOpData.OperationManager = null;
                    if (myOpData.EntryManager != null && await myOpData.EntryManager.Next(myOpData.EntryManager))
                        myOpData.EntryManager = null;
                    if (myOpData.ExitManager != null && await myOpData.ExitManager.Next(myOpData.ExitManager))
                        myOpData.ExitManager = null;
                    //for (int i = 0; i < myOpData.ScheduledTasks.Count; i++)
                    //{
                    //    var task = myOpData.ScheduledTasks[i];
                    //    if (Algo.Time >= task.Time)
                    //    {
                    //         var terminate = await task.Next(task);
                    //        if (terminate)
                    //            myOpData.ScheduledTasks.RemoveAt(i--);
                    //    }
                    //}
                }
            }
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

        private async Task<bool> LiquidateOperation(DeferredTask self)
        {
            var op = self.Op;
            bool terminate = false;
            if (op.AmountRemaining > 0)
            {
                //immediatly liquidate everything with a market order 
                var liquidationResult = await Algo.TryLiquidateOperation(op, self.LiquidateReason);
                if (liquidationResult.order != null)
                {
                    self.myOpData.CurrentExitOrder = liquidationResult.order;
                    await CloseQueueAsync(op, CloseQueueTime);
                    terminate = true;
                }
                else if (liquidationResult.amountRemainingLow)
                {
                    Logger.Info($"Queue for close {op.ToString("c")} because amount remaining is too low");
                    await this.CloseQueueAsync(op, CloseQueueTime);
                    terminate = true;
                }
            }
            else
            {
                await CloseQueueAsync(op, CloseQueueTime);
                terminate = true;
            }
            return terminate;
        }

        private async Task<bool> MonitorOperation(DeferredTask self)
        {
            var myOpData = self.myOpData;
            Operation op = self.Op;
            SymbolData symData = self.SymbolData;

            //if operation is closing or closed we terminate the task chain
            if (op.IsClosing || op.IsClosed)
            {
                if (op.AmountRemaining > 0)
                    Logger.Warn($"Operation {op} is closing but amountremaining is > 0");
                return true;
            }

            //if signal entry is expired and we yet didn't get to enter, then we can just   
            //      queue the operation for close 
            bool isEntryExpired = op.IsEntryExpired(Algo.Time);
            bool noActiveExit = self.myOpData.NoActiveExit();

            //chek if the amout remaining is small ( so need to close the operation )
            bool remainingAmountSmall = true;
            if (op.AmountRemaining > 0)
            {
                var (_, amount) = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, op.Signal.PriceTarget);
                remainingAmountSmall = amount <= 0 && !myOpData.HasExitOrder;
            }

            //queue operation for close if conditions are met
            if (isEntryExpired && noActiveExit && remainingAmountSmall)
            {
                var entryClosed = await CloseEntryOrder(self.Op, self.myOpData);
                var exitClosed = await CloseExitOrder(self.Op, self.myOpData);
                self.Time = Algo.Time.AddSeconds(30);
                if (entryClosed && exitClosed)
                {
                    //stop entries and exits
                    myOpData.EntryManager = null;
                    myOpData.ExitManager = null;

                    //put in close queue
                    if (op.AmountRemaining > 0)
                        Logger.Info($"Schedule operation for close {op.ToString("c")} as amount remaining is low (pt {op.Signal.PriceTarget}).");
                    else
                        Logger.Debug($"Queue for close {self.Op.ToString("c")}\n    because amount remaining is 0 and entry expired.");
                    await CloseQueueAsync(self.Op, CloseQueueTime);
                }
                else
                {
                    self.Time = Algo.Time.AddSeconds(20);
                    self.Next = MonitorOperation;
                }
                return exitClosed && exitClosed;
            }

            //if signal exit is expired 
            //      then we must exit any pending order and liquidate everything with a market order
            if (op.IsExitExpired(Algo.Time))
            {
                //stop entries and exits
                myOpData.EntryManager = null;
                myOpData.ExitManager = null;

                //set next step to close orders and liquidate operation
                self.Next = new DeferredTaskDelegate(CloseOrdersAndLiquidate);
                self.LiquidateReason = " exit deadtime elapsed.";
                //also call next step immediatly only during backtesting
                if (Algo.BackTesting)
                    return await self.Next.Invoke(self);
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
                    Logger.Warn("op is not closing but amountremaining is > 0");
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
                                Logger.Info($"{Algo.Time} - Setting EXIT order for oper {op} - amount:{adj.amount} - price: {adj.price}");
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

                                var request = await Algo.Market.PostNewOrder(orderInfo);

                                if (request.IsSuccessful)
                                {
                                    myOpData.CurrentExitOrder = request.Result;
                                    self.Next = MonitorExit;
                                }
                                else
                                {
                                    //order failed, retry in 10 seconds
                                    Logger.Error("Failed setting exit order:" + request.ErrorInfo.Replace("\n", "\n\t"));
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
                var wrongPrice = Math.Abs(myOpData.CurrentExitOrder.Price - op.Signal.PriceTarget) / op.Signal.PriceTarget > 0.01m;
                if (wrongPrice || wrongAmout || Algo.Time > op.Signal.ExpireDate)
                {
                    Logger.Debug($"Cancelling exit order for operation {op} - wrongAmout: {wrongAmout} - wrongPrice: {wrongPrice} ");
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
                    Logger.Warn("op is not closing but amountremaining is > 0");
                return true;
            }

            if (!op.IsEntryExpired(Algo.Time) && myOpData.CurrentEntryOrder == null)
            {
                //--- open a new entry if needed ---
                var entryNear = op.Signal.Kind == SignalKind.Buy ?
                    ((decimal)symData.Feed.Bid - op.Signal.PriceEntry) / op.Signal.PriceEntry < EntryNearThreshold :
                    (op.Signal.PriceEntry - (decimal)symData.Feed.Ask) / op.Signal.PriceEntry < EntryNearThreshold;

                if (entryNear && !Algo.EntriesSuspended)
                {
                    var originalAmount = AssetAmount.Convert(op.AmountTarget, op.Symbol.Asset, symData.Feed);
                    var stillToBuy = originalAmount - op.AmountInvested;
                    if (stillToBuy / originalAmount > 0.2m)
                    {
                        //assure to enter a limit order ( doesn't execute immediatly
                        var price = op.EntryTradeDirection == TradeDirection.Buy ?
                            Math.Min(op.Signal.PriceEntry, (decimal)self.SymbolData.Feed.Ask) :
                            Math.Max(op.Signal.PriceEntry, (decimal)self.SymbolData.Feed.Bid);

                        //adjust price 
                        var adjusted = symData.Feed.GetOrderAmountAndPriceRoundedDown(stillToBuy, price);
                        adjusted = Algo.ClampOrderAmount(symData, op.EntryTradeDirection, adjusted);
                        if (adjusted.amount / originalAmount > 0.1m)
                        {
                            Logger.Info($"{Algo.Time} - Setting Entry for {op} - amount: {adjusted.amount:0.########} - price: {adjusted.price:0.########}");
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
                            var req = await Algo.Market.PostNewOrder(orderInfo);

                            if (req.IsSuccessful)
                            {
                                // register operation and return to ManageEntry
                                myOpData.CurrentEntryOrder = req.Result;
                                self.Next = MonitorEntry;
                            }
                            else
                            {
                                //log error and repeat operation in 30 seconds
                                Logger.Error($"{Algo.Time} - Failed opening entry order for operation {op.Id} - symbol {op.Symbol} - error: " + req.ErrorInfo.Replace("\n", "\n\t"));
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
                var badPrice = Math.Abs(myOpData.CurrentEntryOrder.Price - priceAdjusted.price) / priceAdjusted.price > 0.006m;
                var entryExpired = op.IsEntryExpired(Algo.Time);

                //N.B. also when signal entry is not valid we keep monitoring as it could be updated
                if (entryDistant || badPrice || entryExpired)
                {
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

        private void ClearTasks(MyOperationData myOpData)
        {
            //for (int i = 0; i < myOpData.ScheduledTasks.Count; i++)
            //{
            //    myOpData.ScheduledTasks[i].Next = new DeferredTaskDelegate((o) => Task.FromResult(true));
            //    myOpData.ScheduledTasks[i].Time = DateTime.MinValue;
            //}
        }

        private async Task<bool> CloseOrder(IOrder order, Operation op)
        {
            bool ok = true;
            if (!order.IsClosed)
            {
                var req = await Algo.Market.OrderCancelAsync(order.Id);
                if (!req.IsSuccessful)
                {
                    //check if order was closed already
                    var req2 = await Algo.Market.OrderSynchAsync(order.Id);
                    if (req2.IsSuccessful)
                        order = req2.Result;
                    else
                    {
                        ok = false;
                        Logger.Error($"Unable to close or synch order {order} in operation {op.ToString()}, errors:" +
                                        $"\n\t{req.ErrorInfo.Replace("\n", "\n\t")}" + $"\n\t{req.ErrorInfo.Replace("\n", "\n\t")}");
                    }

                    if (!order.IsClosed)
                    {
                        ok = false;
                        Logger.Error($"Unable to close order {order} in operation {op.ToString()}, errors:" +
                                       $"\n\t{req.ErrorInfo.Replace("\n", "\n\t")}");
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

        internal delegate Task<bool> DeferredTaskDelegate(DeferredTask self);

        internal class DeferredTask
        {
            public dynamic State = new ExpandoObject();
            public MyOperationData myOpData;
            public Operation Op;
            public DateTime Time { get; set; }
            public DeferredTaskDelegate Next { get; set; }
            public SymbolData SymbolData { get; internal set; }
            public string LiquidateReason { get; internal set; }
        }
    }
}
