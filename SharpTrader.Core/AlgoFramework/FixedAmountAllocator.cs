using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace SharpTrader.AlgoFramework
{
    public class FixedAmountAllocator : FundsAllocator
    {
        private List<Signal> PendingSignals = new List<Signal>(); 
        public TimeSpan CoolDown { get; private set; } = TimeSpan.FromMinutes(30);
        public bool ProportionalToProfit { get; set; } = false;
        public decimal TargetProfit { get; set; } = 0.05m;
        public int MaxActiveOperationsPerSymbol { get; set; } = 1;
        public decimal MaxInvested { get; set; } = 0;
        public decimal MaxInvestedPerSymbol { get; set; } = 0;
        public AssetAmount Amount { get; set; }  

        public FixedAmountAllocator( )
        { 
        } 

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
                    if (ProportionalToProfit)
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
                            var newOper = new Operation( Algo.GetNewOperationId(), signal, new AssetAmount(Amount.Asset, budget), operType);
                            newOper.OnNewTrade += (o, t) => { (symData.AllocatorData as MySymbolData).LastInvestmentTime = t.Time; };
                            slice.Add(newOper); 
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
