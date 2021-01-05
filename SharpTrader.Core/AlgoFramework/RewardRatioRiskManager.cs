using LiteDB;
using SharpTrader.Indicators;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class RewardRatioRiskManager : RiskManager
    {
        public class MySymbolData
        {
            //todo warmup indicators
            [BsonIgnore]
            public Min BaseLevel { get; set; }

            [BsonIgnore]
            public RollingWindow<IBaseData> BaseLevelRecords { get; set; } = new RollingWindow<IBaseData>(60 * 24 * 8);

            public void Reset()
            {
                BaseLevel?.Reset();
            }
        }

        class MyOperationData : IChangeTracking
        {
            private volatile bool _IsChanged = true;
            private IOrder lastExit;
            private double stopLossDelta;
            private double highestPrice;

            public IOrder LastExit
            {
                get => lastExit;
                set { lastExit = value; _IsChanged = true; }
            }
            public double StopLossDelta
            {
                get => stopLossDelta;
                set { stopLossDelta = value; _IsChanged = true; }
            }
            public double HighestPrice
            {
                get => highestPrice;
                set { highestPrice = value; _IsChanged = true; }
            }

            [BsonIgnore] public bool IsChanged => _IsChanged;

            public void AcceptChanges()
            {
                _IsChanged = false;
            }
        }

        private NLog.Logger Logger = NLog.LogManager.GetLogger(nameof(RewardRatioRiskManager));
        public double RiskRewardRatio { get; set; } = 1;
        public TimeSpan BaseLevelTimespan { get; set; } = TimeSpan.Zero;
        public bool TrailingStopLoss { get; set; } = false;

        private MySymbolData GetData(SymbolData symData)
        {
            MySymbolData mydata = symData.RiskManagerData as MySymbolData;
            if (mydata == null)
            {
                mydata = new MySymbolData();
                if (BaseLevelTimespan > TimeSpan.Zero)
                    mydata.BaseLevel = new Min("Min", (int)BaseLevelTimespan.TotalMinutes);
                symData.RiskManagerData = mydata;
            }
            return mydata;
        }

        private MyOperationData GetData(Operation operation)
        {
            var myOpData = operation.RiskManagerData as MyOperationData;
            if (myOpData == null)
            {
                double CalcStopLoss(Operation op)
                {
                    var reward = (double)Math.Abs(op.Signal.PriceTarget - op.Signal.PriceEntry);
                    if (op.EntryTradeDirection == TradeDirection.Buy)
                        return -reward * RiskRewardRatio;
                    else
                        return reward * RiskRewardRatio;
                }

                myOpData = new MyOperationData()
                {
                    StopLossDelta = CalcStopLoss(operation),
                };
                operation.Signal.OnModify += (signal) => CalcStopLoss(signal.Operation);
                operation.RiskManagerData = myOpData;

            }
            return myOpData;
        }

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override async Task Update(TimeSlice slice)
        {
            foreach (var op in Algo.ActiveOperations.Where(o => o.IsStarted() && !o.IsClosed && !o.IsClosing))
            {
                if (op.AmountRemaining <= 0)
                    op.ScheduleClose(Algo.Time + TimeSpan.FromMinutes(2));

                var mySymData = GetData(Algo.SymbolsData[op.Symbol]);
                var opData = GetData(op);

                //---
                if (op.AmountRemaining > 0 && !op.RiskManaged)
                {
                    var stopLossPrice = TrailingStopLoss ? opData.HighestPrice + opData.StopLossDelta : (double)op.Signal.PriceEntry + opData.StopLossDelta;
                    //get gain percent
                    if (op.Type == OperationType.BuyThenSell)
                    {
                        var stopLossReached = Algo.SymbolsData[op.Symbol.Key].Feed.Bid < stopLossPrice;
                        if (mySymData.BaseLevel != null) //BaseLevel is null if it is disabled by setting length to 0
                            stopLossReached |= Algo.SymbolsData[op.Symbol.Key].Feed.Bid < mySymData.BaseLevel.Value;
                        if (stopLossReached)
                            op.RiskManaged = true;
                    }
                    else if (op.Type == OperationType.SellThenBuy)
                    {
                        if (Algo.SymbolsData[op.Symbol.Key].Feed.Ask > stopLossPrice)
                            op.RiskManaged = true;
                    }
                }

                if (op.RiskManaged && !(op.IsClosed || op.IsClosing))
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

            foreach (var skv in slice.SymbolsData)
            {
                var symData = Algo.SymbolsData[skv.Key];
                var mySymData = GetData(symData);
                if (mySymData.BaseLevel != null)
                {
                    foreach (var rec in skv.Value.Records)
                    {
                        foreach (var op in symData.ActiveOperations)
                        {
                            if (op.AmountInvested > 0)
                            {
                                var opData = GetData(op);
                                if (rec.High > opData.HighestPrice)
                                    opData.HighestPrice = rec.High;
                            }
                        }

                        mySymData.BaseLevel.Update((ITradeBar)rec);
                        if (Algo.IsPlottingEnabled)
                        {
                            mySymData.BaseLevelRecords.Add(mySymData.BaseLevel.Current);
                        }
                    }
                }
            }
        }
    }

}
