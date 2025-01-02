using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Diagnostics;

namespace SharpTrader.AlgoFramework
{
    public class FixedAmountAllocator : FundsAllocator
    {
        public TimeSpan CoolDown { get; set; } = TimeSpan.FromMinutes(0);
        public bool ProportionalToProfit { get; set; } = false;
        public decimal TargetProfit { get; set; } = 0.05m;
        public int MaxActiveOperationsPerSymbol { get; set; } = 1;
        public int MaxOperationsWithPendingEntry { get; set; } = 1;
        public decimal Budget { get; set; } = 0;
        public decimal BudgetPerSymbol { get; set; } = 0;
        public AssetAmount BudgetPerOperation { get; set; }

        public FixedAmountAllocator()
        {
        }

        public override Task Update(TimeSlice slice)
        {
            //check the free budget - the used budget is the sum of all money still invested in operations
            var allocatedBudget = Algo.ActiveOperations.Where(o => o.IsActive).Sum(o =>
                                AssetAmount.Convert(o.AmountTarget, BudgetPerOperation.Asset, o.Symbol, o.Signal.PriceEntry));
            var freeBudget = Budget - allocatedBudget;
            if (freeBudget > 0)
            {
                Algo.ResumeEntries();
                var signalsOrderedByPriority = slice.NewSignals.OrderByDescending(s => s.Priority);
                //for each signal allocate a fixed amount
                foreach (Signal signal in signalsOrderedByPriority)
                {
                    var newAmount = BudgetPerOperation.Amount;
                    if (ProportionalToProfit)
                    {
                        var profit = Math.Abs(signal.PriceTarget - signal.PriceEntry) / signal.PriceEntry;
                        newAmount = newAmount * TargetProfit / profit;
                    }

                    var symData = Algo.SymbolsData[signal.Symbol.Key];
                    if (symData.AllocatorData == null)
                        symData.AllocatorData = new MySymbolData();

                    DateTime lastInvestment = (symData.AllocatorData as MySymbolData).LastInvestmentTime;
                    //NOTICE MaxActiveOperationsPerSymbol is a problem if we get a new signal because we ignore the signale if there is an operation 
                    //     with bad signal tied to it. So it is important that the sentry modifies the signal tied to the last active operation if 
                    //     this limit is enabled
                    if (Algo.Time >= lastInvestment + CoolDown && symData.ActiveOperations.Count < this.MaxActiveOperationsPerSymbol)
                    {
                        int operationsWaitingForEntry = symData.ActiveOperations.Count(o => o.IsActive && o.AmountInvested == 0);
                        if (operationsWaitingForEntry < MaxOperationsWithPendingEntry)
                        {
                            //if cooldown has elapsed we can open a new operation
                            var freeSymbolBudget = BudgetPerSymbol - Algo.Executor.GetInvestedOrLockedAmount(signal.Symbol, BudgetPerOperation.Asset);

                            var budget = new[] { freeSymbolBudget, freeBudget, newAmount }.Min();

                            if (budget >= 0.2m * newAmount)
                            {
                                //create operations
                                var operType = signal.Kind == SignalKind.Buy ? OperationType.BuyThenSell : OperationType.SellThenBuy;
                                var newOper = new Operation(
                                    Algo.GetNewOperationId(),
                                    signal,
                                    new AssetAmount(BudgetPerOperation.Asset, budget),
                                    operType);
                                freeBudget -= budget;
                                newOper.OnNewTrade += (o, t) =>
                                {
                                    if (t.Direction == o.EntryTradeDirection)
                                        (symData.AllocatorData as MySymbolData).LastInvestmentTime = t.Time;
                                };
                                slice.Add(newOper);
                            }
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }

        class MySymbolData
        {
            public DateTime LastInvestmentTime { get; set; }
        }
    }

}
