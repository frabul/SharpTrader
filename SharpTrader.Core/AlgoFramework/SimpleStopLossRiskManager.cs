using LiteDB;
using System;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class SimpleStopLossRiskManager : RiskManager
    {
        class RMData
        {
            public IOrder LastExit { get; set; }
        }
        public decimal StopLoss { get; private set; }
        private NLog.Logger Logger;
        public SimpleStopLossRiskManager(decimal stopLoss)
        {
            Logger = NLog.LogManager.GetCurrentClassLogger();
            StopLoss = stopLoss;
        }

        public override void RegisterSerializationHandlers(BsonMapper mapper)
        {
            mapper.RegisterType<RMData>(o => mapper.Serialize(o), bson => mapper.Deserialize<RMData>(bson));
        }
        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
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
                    if (op.AmountRemaining > 0)
                    {
                        //--- liquidate operation funds ---
                        var lr = await Algo.TryLiquidateOperation(op, $"stopLoss reached");
                        if (lr.amountRemainingLow)
                        {
                            if (op.AmountRemaining / op.AmountInvested < 0.07m) //todo gestire meglio
                            {
                                Logger.Info($"Schedule operation for close {op.ToString("c")} as amount remaining is low.");
                                op.ScheduleClose(Algo.Time.AddMinutes(3));
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

}
