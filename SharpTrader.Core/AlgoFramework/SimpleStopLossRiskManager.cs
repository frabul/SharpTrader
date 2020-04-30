using LiteDB;
using SharpTrader.Indicators;
using System;
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

        class MyOperationData
        {
            public IOrder LastExit { get; set; }
            public double StopLossDelta { get; set; }
            public double HighestPrice { get; set; }

        }

        public double RiskRewardRatio { get; set; } = 1;
        public TimeSpan BaseLevelTimespan { get; set; } = TimeSpan.Zero;
        public bool TrailingStopLoss { get; set; } = false;


        public override void RegisterSerializationMappers(BsonMapper mapper)
        {
            BsonMapper defaultMapper = new BsonMapper();
            mapper.RegisterType<MyOperationData>(o => defaultMapper.Serialize(o), bson => mapper.Deserialize<MyOperationData>(bson));
            mapper.RegisterType<MySymbolData>(o => defaultMapper.Serialize(o), bson => mapper.Deserialize<MySymbolData>(bson));
        }


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
                if (!op.RiskManaged)
                {
                    var stopLossPrice = TrailingStopLoss ? opData.HighestPrice + opData.StopLossDelta : (double)op.Signal.PriceEntry + opData.StopLossDelta;
                    //get gain percent
                    if (op.Type == OperationType.BuyThenSell)
                    {
                        var sl1 = Algo.SymbolsData[op.Symbol.Key].Feed.Bid < stopLossPrice;
                        var sl2 = mySymData.BaseLevel == null || Algo.SymbolsData[op.Symbol.Key].Feed.Bid < mySymData.BaseLevel.Value;
                        if (sl1 || sl2)
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
                    TradeDirection orderDirection;
                    if (op.Type == OperationType.BuyThenSell)
                        orderDirection = TradeDirection.Sell;
                    else if (op.Type == OperationType.SellThenBuy)
                        orderDirection = TradeDirection.Buy;
                    else
                        throw new NotSupportedException();

                    //--- liquidate operation funds ---
                    await Algo.Executor.CancelAllOrders(op);
                    var request = await Algo.Market.MarketOrderAsync(op.Symbol.Key, orderDirection, op.AmountRemaining, op.GetNewOrderId());

                    if (request.IsSuccessful)
                    {
                        //expect trade that will close the operation
                        opData.LastExit = request.Result;
                        //warning - we just requested a trade, the new trade will cancel our close request!!
                        op.ScheduleClose(Algo.Time.AddMinutes(5));
                    }
                    else
                    {
                        //todo log error
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

    public class SimpleStopLossRiskManager : RiskManager
    {
        class RMData
        {
            public IOrder LastExit { get; set; }
        }
        public decimal StopLoss { get; private set; }
        public SimpleStopLossRiskManager(decimal stopLoss)
        {
            StopLoss = stopLoss;
        }

        public override void RegisterSerializationMappers(BsonMapper mapper)
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
                if (!op.RiskManaged)
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

                if (op.RiskManaged)
                {
                    TradeDirection orderDirection;
                    if (op.Type == OperationType.BuyThenSell)
                        orderDirection = TradeDirection.Sell;
                    else if (op.Type == OperationType.SellThenBuy)
                        orderDirection = TradeDirection.Buy;
                    else
                        throw new NotSupportedException();

                    //add trades correlated to this order
                    foreach (ITrade trade in slice.SymbolsData[op.Symbol.Key].Trades)
                    {
                        if (trade.Direction == orderDirection)
                            op.AddExit(trade);
                        else
                            op.AddEntry(trade);
                    }

                    //--- liquidate operation funds ---
                    await Algo.Executor.CancelAllOrders(op);
                    var request = await Algo.Market.MarketOrderAsync(op.Symbol.Key, orderDirection, op.AmountRemaining, op.GetNewOrderId());

                    if (request.IsSuccessful)
                    {
                        //expect trade that will close the operation
                        (op.RiskManagerData as RMData).LastExit = request.Result;
                    }
                    else
                    {
                        //todo log error
                    }
                }
            }
        }
    }

}
