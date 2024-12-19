using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    /// Ignores cooldown and allocates a new Operation if the entry 
    ///    - for BuyThenSell: is x% lower than the lowest entry price of the active operations
    ///    - for SellThenBuy: is x% higher than the lowest entry price of the active operations
    /// </summary>
    public class DoubleDownAllocator : FixedAmountAllocator
    {

        public bool ProportionalToProfit { get; set; } = false;
        public decimal TargetProfit { get; set; } = 0.05m;
        public int MaxActiveOperationsPerSymbol { get; set; } = 1;
        public int MaxOperationsWithPendingEntry { get; set; } = 1;
        public decimal Budget { get; set; } = 0;
        public decimal BudgetPerSymbol { get; set; } = 0;
        public AssetAmount BudgetPerOperation { get; set; }
        public decimal DoubleDownThreshold { get; set; } = 0.01m;

        public DoubleDownAllocator()
        {
        }

        bool ShouldDoubleDown(Signal signal, SymbolData symData)
        {
            // if there are no active operations we can double down
            var activeOperations = symData.ActiveOperations.Where(o => o.Signal.Kind == signal.Kind).ToArray();
            if (activeOperations.Length == 0)
                return true;

            if (signal.Kind == SignalKind.Buy)
            {
                var lowestEntryPrice = activeOperations.Min(o => o.AverageEntryPrice);
                var diff = (lowestEntryPrice - signal.PriceEntry) / lowestEntryPrice;
                return diff > DoubleDownThreshold;
            }
            else if (signal.Kind == SignalKind.Sell)
            {
                var highestEntryPrice = activeOperations.Max(o => o.AverageEntryPrice);
                var diff = (signal.PriceEntry - highestEntryPrice) / highestEntryPrice;
                return diff > DoubleDownThreshold;
            }
            else
                return false;

        }
        public override Task Update(TimeSlice slice)
        {

            decimal StillInvestedGetter(Operation o)
            {
                if (o.Symbol.Asset == BudgetPerOperation.Asset)
                    return o.AmountRemaining;
                else if (o.Symbol.QuoteAsset == BudgetPerOperation.Asset)
                    return o.QuoteAmountRemaining;
                else
                    throw new NotSupportedException("Only supported operations where asset or quoteAsset coincide with budget asset");
            }

            //check the free budget - the used budget is the sum of all money still invested in operations
            var totalInvested = Algo.ActiveOperations.Sum(StillInvestedGetter);
            var freeBudged = Budget - totalInvested;
            if (freeBudged <= 0)
            {
                Algo.StopEntries();
            }
            else
            {
                Algo.ResumeEntries();

                //for each signal allocate a fixed amount
                foreach (Signal signal in slice.NewSignals)
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
                    var lowestEntryPrice = symData.ActiveOperations.Min(o => o.AverageEntryPrice);
                    //todo MaxActiveOperationsPerSymbol is a problem if we gat a new signal because we ignore it if there is another operation 
                    //     with bad signal
                    if (Algo.Time >= lastInvestment + CoolDown && symData.ActiveOperations.Count < this.MaxActiveOperationsPerSymbol)
                    {

                        //if cooldown has elapsed we can open a new operation
                        int operationsWaitingForEntry = symData.ActiveOperations.Count(o => o.IsActive && o.AmountInvested == 0);
                        if (operationsWaitingForEntry < MaxOperationsWithPendingEntry)
                        {
                            //if cooldown has elapsed we can open a new operation
                            var freeSymbolBudget = BudgetPerSymbol - Algo.Executor.GetInvestedOrLockedAmount(signal.Symbol, BudgetPerOperation.Asset);

                            var budget = new[] { freeSymbolBudget, freeBudged, newAmount }.Min();

                            if (budget >= 0.2m * newAmount)
                            {
                                //create operations
                                var operType = signal.Kind == SignalKind.Buy ? OperationType.BuyThenSell : OperationType.SellThenBuy;
                                var newOper = new Operation(Algo.GetNewOperationId(), signal, new AssetAmount(BudgetPerOperation.Asset, budget), operType);
                                newOper.OnNewTrade += (o, t) => { (symData.AllocatorData as MySymbolData).LastInvestmentTime = t.Time; };
                                slice.Add(newOper);
                            }
                        }

                    }
                }
                //todo it is possible that for a given symbol some budget get freed , in this case we should allocate this margin to existent operations
            }
            return Task.CompletedTask;
        }

        class MySymbolData
        {
            public DateTime LastInvestmentTime { get; set; }
        }
    }


}