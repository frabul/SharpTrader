using LiteDB;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class SimpleStopLossRiskManager : RiskManager
    {
        public class RMData : IChangeTracking
        {
            public DateTime NextTry { get; set; } = DateTime.MinValue;
            public int LiquidationTries { get; set; }
            public bool IsChanged => false;
            public void AcceptChanges()
            {

            }
        }

        public decimal StopLoss { get; private set; }
        private NLog.Logger Logger;
        public SimpleStopLossRiskManager(decimal stopLoss)
        {
            Logger = NLog.LogManager.GetLogger("SimpleStopLossRiskManager");
            StopLoss = stopLoss;
        }

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        private RMData GetData(Operation op)
        {
            if (op.RiskManagerData is RMData myData)
                return myData;
            op.RiskManagerData = new RMData();
            return op.RiskManagerData as RMData;
        }

        public override async Task Update(TimeSlice slice)
        {

            foreach (var op in Algo.ActiveOperations)
            {

                //---
                if (op.AmountRemaining > 0 && !op.RiskManaged)
                {
                    //get gain percent
                    if (op.Type == OperationType.BuyThenSell)
                    {
                        var loss = -((decimal)Algo.SymbolsData[op.Symbol.Key].Feed.Bid - op.AverageEntryPrice) / op.AverageEntryPrice;
                        if (loss >= StopLoss)
                            op.RiskManaged = true;
                    }
                    else if (op.Type == OperationType.SellThenBuy)
                    {
                        var loss = -(op.AverageEntryPrice - (decimal)Algo.SymbolsData[op.Symbol.Key].Feed.Ask) / op.AverageEntryPrice;
                        if (loss >= StopLoss)
                            op.RiskManaged = true;
                    }
                }

                if (op.RiskManaged && !(op.IsClosing || op.IsClosed))
                {
                    var myData = GetData(op);

                    if (Algo.Time > myData.NextTry && op.AmountRemaining > 0)
                    {
                        //--- liquidate operation funds ---
                        var lr = await Algo.TryLiquidateOperation(op, $"stopLoss reached");
                        //if (lr.amountRemainingLow)
                        //{
                        //if (op.AmountRemaining / op.AmountInvested < 0.1m) //todo gestire meglio
                        //    {
                        //        Logger.Info($"Schedule operation for close {op.ToString("c")} as amount remaining is low.");
                        //        op.ScheduleClose(Algo.Time.AddMinutes(3));
                        //    }
                        //}
                        myData.LiquidationTries++; 
                        var delaySeconds = 120 + Math.Min(8, myData.LiquidationTries) * 60;
                        myData.NextTry = Algo.Time.AddSeconds(delaySeconds);
                        if (lr.amountRemainingLow || lr.OrderError)
                        {
                            Logger.Info($"Liquidation failed for {op.ToString("c")}, tries count: {myData.LiquidationTries}, retrying in {delaySeconds}s ");
                        }
                        if (myData.LiquidationTries > 50)
                        {
                            Logger.Info($"Schedule operation for close {op.ToString("c")} as it reached maximum number of liquidation tries.");
                        } 
                    }
                    else
                    {
                        Logger.Info($"Queueing for close operation {op.ToString("c")}");
                        op.ScheduleClose(Algo.Time.AddMinutes(3));
                    }
                }
            }
        }
    }

}
