using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace SharpTrader.AlgoFramework
{
    public class FixedAmountAllocator : FundsAllocator
    {
        private List<Signal> PendingSignals = new List<Signal>();
        private int TotalOperations = 0;
        public TimeSpan CoolDown = TimeSpan.FromMinutes(30);
        public bool ProportinalToProfit = true;
        public decimal TargetProfit { get; set; } = 0.05m;
        public int MaxActiveOperationsPerSymbol { get; set; } = 1;
        public FixedAmountAllocator(AssetSum amount, decimal maxInvestedPerSymbol, decimal maxInvested)
        {
            MaxInvested = maxInvested;
            MaxInvestedPerSymbol = maxInvestedPerSymbol;
            Amount = amount;
        }

        public decimal MaxInvested { get; set; }
        public decimal MaxInvestedPerSymbol { get; set; }
        public AssetSum Amount { get; set; }

        public override void Update(TimeSlice slice)
        {
            decimal StillInvestedGetter(Operation o)
            {
                if (o.Symbol.Asset == Amount.Asset)
                    return o.AmountRemaining;
                else if (o.Symbol.QuoteAsset == Amount.Asset)
                    return o.QuoteAmountRemaining;
                else
                    throw new NotSupportedException("Only supported operations where asset or quoteAsset coincide with budget asset");
            }

            //check the free budget - the used budget is the sum of all money still invested in operations
            var totalInvested = Algo.ActiveOperations.Sum(StillInvestedGetter);
            var freeBudged = MaxInvested - totalInvested;
            if (freeBudged <= 0)
            {
                Algo.StopEntries();
                PendingSignals.AddRange(slice.NewSignals);
            }
            else
            {
                Algo.ResumeEntries();

                //for each signal allocate a fixed amount
                foreach (Signal signal in slice.NewSignals)
                {
                    var newAmount = Amount.Amount;
                    if (ProportinalToProfit)
                    {
                        var profit = Math.Abs(signal.PriceTarget - signal.PriceEntry) / signal.PriceEntry;
                        newAmount = newAmount * TargetProfit / profit;
                    }

                    var symData = Algo.SymbolsData[signal.Symbol.Key];
                    if (symData.AllocatorData == null)
                        symData.AllocatorData = new MySymbolData();

                    DateTime lastInvestment = (symData.AllocatorData as MySymbolData).LastInvestmentTime;

                    if (Algo.Time >= lastInvestment + CoolDown && symData.ActiveOperations.Count < this.MaxActiveOperationsPerSymbol)
                    {
                        //if cooldown has elapsed we can open a new operation
                        var freeSymbolBudget = MaxInvestedPerSymbol - Algo.Executor.GetInvestedOrLockedAmount(signal.Symbol, Amount.Asset);

                        var budget = new[] { freeSymbolBudget, freeBudged, newAmount }.Min();

                        if (budget >= 0.2m * newAmount)
                        {
                            //create operations
                            var operType = signal.Kind == SignalKind.Buy ? OperationType.BuyThenSell : OperationType.SellThenBuy;
                            var newOper = new Operation(TotalOperations.ToString(), signal, new AssetSum(Amount.Asset, budget), operType);
                            newOper.OnNewTrade += (o, t) => { (symData.AllocatorData as MySymbolData).LastInvestmentTime = t.Time; };
                            slice.Add(newOper);

                            TotalOperations++;
                        }
                        else
                        {
                            PendingSignals.Add(signal);
                        }
                    }
                }
                //todo it is possible that for a given symbol some budget get freed , in this case we should allocate this margin to existent operations
            }
        }

        class MySymbolData
        {
            public DateTime LastInvestmentTime { get; set; }
        }
    }

}
