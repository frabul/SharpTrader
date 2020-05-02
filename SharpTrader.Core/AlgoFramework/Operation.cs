﻿using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace SharpTrader.AlgoFramework
{
    public enum OperationType
    {
        BuyThenSell,
        SellThenBuy,
    }

    public interface IJObjectConvertible
    {
        JObject ToJobject(TradingAlgo algo);
    }

    public class Operation
    {
        public event Action<Operation, ITrade> OnNewTrade;
        public event Action<Operation> OnClosing;
        public event Action<Operation> OnClosed;
        public event Action<Operation> OnResumed;

        //todo prevedere la possibile chiusura di una operazione passando i fondi ad un'altra 

        private Signal _Signal;
        private HashSet<ITrade> _Entries = new HashSet<ITrade>();
        private HashSet<ITrade> _Exits = new HashSet<ITrade>();
         
        public int OrdersCount { get; private set; } = 0;
        /// <summary>
        /// Unique identifier for this operation
        /// </summary> 
        public string Id { get; private set; }

        [BsonIgnore] public DateTime CreationTime => Signal.CreationTime;

        /// <summary>
        /// Signal associated with the operation
        /// </summary>
        public Signal Signal
        {
            get => _Signal;
            private set
            {
                System.Diagnostics.Debug.Assert(value.Operation == null);
                _Signal = value;
                _Signal.Operation = this;
                _Signal.OnModify += (s) => Resume();
            }
        }
        public AssetAmount AmountTarget { get; private set; }
        public decimal AverageEntryPrice { get; private set; }
        public decimal AverageExitPrice { get; private set; }
        public decimal AmountInvested { get; private set; }
        public decimal QuoteAmountInvested { get; private set; }
        public decimal AmountLiquidated { get; private set; }
        public decimal QuoteAmountLiquidated { get; private set; }
        public decimal AmountRemaining { get; private set; }
        public decimal QuoteAmountRemaining { get; private set; }
        public OperationType Type { get; private set; }
        [BsonIgnore] public SymbolInfo Symbol => Signal.Symbol;
        public bool RiskManaged { get; set; }
        public object ExecutorData { get; set; }
        public object RiskManagerData { get; set; }
        public TradeDirection EntryTradeDirection { get; private set; }
        public TradeDirection ExitTradeDirection { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsClosing { get; private set; }
        public DateTime CloseDeadTime { get; private set; } = DateTime.MaxValue;
        public DateTime LastInvestmentTime { get; private set; }
        /// <summary>
        /// All trades associated with this operation
        /// </summary> 
        [BsonIgnore]
        public IEnumerable<ITrade> AllTrades
        {
            get => _Entries.Concat(Exits);
       
             
        }
        /// <summary>
        /// Trades that were meant as entries
        /// </summary>
        public IEnumerable<ITrade> Entries { get => _Entries; private set => _Entries = new HashSet<ITrade>(value); }//private setter used by serialization
        /// <summary>
        /// Trades that were meant as exits
        /// </summary>
        public IEnumerable<ITrade> Exits { get => _Exits; private set => _Exits = new HashSet<ITrade>(value); }//private setter used by serialization



        public Operation( )
        {
        
        }

        public Operation(string id, Signal signal, AssetAmount amountTarget, OperationType type)
        {
            this.Id = id;
            this.Signal = signal;
            this.AmountTarget = amountTarget;
            this.Type = type;
            SetTradesDirections();

        }

        public Operation(JObject me)
        {
            //this.OrdersCount = me["OrdersCount"].ToObject<int>();
            //this.Algo = algo;
            //this.Id = me["Id"].ToObject<string>();
            ////this.Signal = Algo.Sentry.DeserializeSignal(me["Signal"]);
            //this.AmountTarget = me["AmountTarget"].ToObject<AssetAmount>();
            //this.Type = me["Type"].ToObject<OperationType>();
            //SetTradesDirections();

            //this.CloseDeadTime = me["CloseDeadTime"].ToObject<DateTime>();
            //this.IsClosed = me["IsClosed"].ToObject<bool>();
            //this.IsClosed = me["IsClosed"].ToObject<bool>();
            //this.RiskManaged = me["RiskManaged"].ToObject<bool>();

            //this.RiskManagerData = Algo.RiskManager.DeserializeOperationData(me["RiskManagerData"]);
            //this.ExecutorData = Algo.Executor.DeserializeOperationData(me["ExecutorData"]);

            ////deserialize trades
            //foreach (var tradeId in me["AllTrades"].ToArray())
            //    this.AddTrade(Algo.Market.GetTradeById(tradeId));
        }

        private void SetTradesDirections()
        {
            if (this.Type == OperationType.BuyThenSell)
            {
                EntryTradeDirection = TradeDirection.Buy;
                ExitTradeDirection = TradeDirection.Sell;
            }
            else if (Type == OperationType.SellThenBuy)
            {
                EntryTradeDirection = TradeDirection.Sell;
                ExitTradeDirection = TradeDirection.Buy;
            }
            else
                throw new NotSupportedException("Only supports buyThenSell and SellThenBuy operations");
        }

        public virtual string GetNewOrderId()
        {
            var orderId = $"{this.Id}-{OrdersCount}";
            OrdersCount++;
            return orderId;
        }

        public bool IsTradeAssociated(ITrade trade)
        {
            return trade.ClientOrderId.StartsWith(this.Id + "-");
        }

        public bool IsEntryExpired(DateTime time)
        {
            return time >= this.Signal.EntryExpiry;
        }

        public bool IsExitExpired(DateTime time)
        {
            return time >= this.Signal.ExpireDate;
        }

        public void UpdateInfo()
        {
            AverageEntryPrice = _Entries.Sum(t => t.Price) / _Entries.Count;
            AmountInvested = _Entries.Sum(t => t.Amount);
            AmountLiquidated = _Exits.Sum(t => t.Amount);
            QuoteAmountRemaining = AmountInvested - AmountLiquidated;
        }

        public void AddEntry(ITrade entry)
        {
            if (this._Entries.Add(entry))
            {
                LastInvestmentTime = entry.Time;
                this.AmountInvested += entry.Amount;
                this.QuoteAmountInvested += entry.Amount * entry.Price;
                this.AverageEntryPrice = ((this.AverageEntryPrice * (_Entries.Count - 1)) + entry.Price) / _Entries.Count;

                this.AmountRemaining = this.AmountInvested - AmountLiquidated;
                this.QuoteAmountRemaining = this.QuoteAmountInvested - QuoteAmountLiquidated;
            }

        }

        public void AddExit(ITrade exit)
        {
            if (this._Exits.Add(exit))
            {
                this.AmountLiquidated += exit.Amount;
                this.QuoteAmountLiquidated += exit.Amount * exit.Price;
                this.AverageExitPrice = ((this.AverageExitPrice * (_Exits.Count - 1)) + exit.Price) / _Exits.Count;

                this.AmountRemaining = this.AmountInvested - AmountLiquidated;
                this.QuoteAmountRemaining = this.QuoteAmountInvested - QuoteAmountLiquidated;
            }
        }

        public bool IsStarted()
        {
            return this.IsClosed || this.IsClosing || this.AmountInvested > 0;
        }

        public void AddTrade(ITrade trade)
        {
            if (trade.Direction == this.EntryTradeDirection)
                this.AddEntry(trade);
            else if (trade.Direction == this.ExitTradeDirection)
                this.AddExit(trade);
            else
                throw new Exception("Unknown trade direction");
            Resume();
            OnNewTrade?.Invoke(this, trade);
        }

        public override string ToString()
        {
            return $"oper {{Id: {Id}, Symbol: {Symbol.Key}, Type: {this.Type}}}";
        }

        public void ScheduleClose(DateTime deadTime)
        {
            if (!IsClosing)
            {
                this.IsClosing = true;
                this.CloseDeadTime = deadTime;
                OnClosing?.Invoke(this);
            }
        }

        public void Close()
        {
            this.IsClosed = true;
            OnClosed?.Invoke(this);
        }

        public void Resume()
        {
            Debug.Assert(this.IsClosed == false);
            if (IsClosing)
            {
                IsClosing = false;
                CloseDeadTime = DateTime.MaxValue;
                OnResumed?.Invoke(this);
            }
        }

        public JObject ToJObject()
        {
            JObject me = new JObject();
            //me["OrdersCount"] = this.OrdersCount;
            //me["AllTrades"] = new JArray(this.AllTrades.Select(tr => tr.Id));
            //me["Id"] = this.Id;
            //me["Type"] = JToken.FromObject(this.Type);
            //me["AmountTarget"] = JObject.FromObject(this.AmountTarget);
            ////me["Signal"] = Algo.Sentry.GetSerializationData(this.Signal);
            //me["CloseDeadTime"] = JToken.FromObject(this.CloseDeadTime);
            //me["IsClosed"] = this.IsClosed;
            //me["IsClosing"] = this.IsClosing;
            //me["RiskManaged"] = this.RiskManaged;
            //me["RiskManagerData"] = Algo.RiskManager.GetSerializationData(this.RiskManagerData);
            //me["ExecutorData"] = Algo.Executor.SerializeOperationData(this.ExecutorData);
            return me;
        }
    }
}