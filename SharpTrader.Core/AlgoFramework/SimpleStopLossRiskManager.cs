using LiteDB;
using SharpTrader.BrokersApi.Binance;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class SimpleStopLossRiskManager : RiskManager
    {
        public class RMData : IChangeTracking
        {
            private DateTime nextTry = DateTime.MinValue;
            private int liquidationTries;
            private volatile bool isChanged;

            public DateTime NextTry { get => nextTry; set { nextTry = value; IsChanged = true; } }
            public int LiquidationTries { get => liquidationTries; set { liquidationTries = value; IsChanged = true; } }
            public bool IsChanged { get => isChanged; private set => isChanged = value; }
            public void AcceptChanges()
            {
                IsChanged = false;
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
                try
                {
                    await ManageOperation(op);
                }
                catch (Exception ex)
                {
                    var msg = BinanceMarketApi.GetExceptionErrorInfo(ex);
                    Logger.Error($"SimpleStopLossRiskManager: exception while managin  {op.ToString("c")}\n       Error: {msg}");
                }
            }
        }

        private async Task ManageOperation(Operation op)
        {
            var symData = Algo.SymbolsData[op.Symbol.Key];
            //---
            if (op.AmountRemaining > 0 && !op.RiskManaged)
            {
                //get gain percent
                if (op.Type == OperationType.BuyThenSell)
                {
                    var loss = -((decimal)symData.Feed.Bid - op.AverageEntryPrice) / op.AverageEntryPrice;
                    if (loss >= StopLoss)
                        op.RiskManaged = true;
                }
                else if (op.Type == OperationType.SellThenBuy)
                {
                    var loss = -(op.AverageEntryPrice - (decimal)symData.Feed.Ask) / op.AverageEntryPrice;
                    if (loss >= StopLoss)
                        op.RiskManaged = true;
                }
            }

            if (op.RiskManaged && !(op.IsClosing || op.IsClosed))
            {
                var myData = GetData(op);

                if (op.AmountRemaining > 0)
                {
                    if (Algo.Time > myData.NextTry)
                    {
                        var (_, amount) = symData.Feed.GetOrderAmountAndPriceRoundedDown(op.AmountRemaining, op.Signal.PriceTarget);
                        bool remainingAmountSmall = (op.AmountInvested == 0 || amount <= 0);
                        if (remainingAmountSmall)
                        {
                            Logger.Info($"Schedule operation for close {op.ToString("c")} as amount remaining is low (pt {op.Signal.PriceTarget}).");
                            op.ScheduleClose(Algo.Time.AddMinutes(3));
                        }
                        else
                        {
                            //--- liquidate operation funds ---
                            var lr = await Algo.TryLiquidateOperation(op, $"stopLoss reached");
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
                                op.ScheduleClose(Algo.Time.AddMinutes(3));
                            }
                        }
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
