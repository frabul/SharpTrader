using LiteDB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    public class Operation : IChangeTracking
    {
        
        public event Action<Operation, ITrade> OnNewTrade;
        public event Action<Operation> OnClosing;
        public event Action<Operation> OnClosed;
        public event Action<Operation> OnResumed;

        //todo prevedere la possibile chiusura di una operazione passando i fondi ad un'altra 

        private Signal _Signal;
        private HashSet<ITrade> _Entries = new HashSet<ITrade>(EntriesEqualityComparer.Instance);
        private HashSet<ITrade> _Exits = new HashSet<ITrade>(EntriesEqualityComparer.Instance);
        private volatile bool _IsChanged = true;

        public int OrdersCount { get; private set; } = 0;
        /// <summary>
        /// Unique identifier for this operation
        /// </summary> 
        public string Id { get; private set; }
        public bool RiskManaged { get; set; }
        public IChangeTracking ExecutorData { get; set; }
        public IChangeTracking RiskManagerData { get; set; }
        public DateTime CreationTime { get; private set; }

        /// <summary>
        /// Signal associated with the operation
        /// </summary>
        public Signal Signal
        {
            get => _Signal;
            private set
            {
                //System.Diagnostics.Debug.Assert(value.Operation == null);
                _Signal = value;
                _Signal.Operation = this;
                _Signal.OnModify += (s) => Resume();
                CreationTime = _Signal.CreationTime;
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

        public TradeDirection EntryTradeDirection { get; private set; }
        public TradeDirection ExitTradeDirection { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsClosing { get; private set; }
        public DateTime CloseDeadTime { get; private set; } = DateTime.MaxValue;
        public DateTime LastInvestmentTime { get; private set; }
        public bool IsChanged => this.Signal.IsChanged || this._IsChanged || ExecutorData.IsChanged || RiskManagerData.IsChanged;

        /// <summary>
        /// All trades associated with this operation
        /// </summary> 
        [BsonIgnore]
        public IEnumerable<ITrade> AllTrades
        {
            get => _Entries.Concat(Exits);
        }

        /// <summary>W
        /// Trades that were meant as entries
        /// </summary>
        public IEnumerable<ITrade> Entries
        {
            get => _Entries; private set
            { 
                foreach (var trade in value)
                    _Entries.Add(trade);
            }
        }//private setter used by serialization

        /// <summaropera
        /// Trades that were meant as exits
        /// </summary>
        public IEnumerable<ITrade> Exits
        {
            get => _Exits;
            private set
            {
                
                foreach (var trade in value)
                    _Exits.Add(trade);
            }
        }//private setter used by serialization


        public Operation()
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

        internal void Recalculate()
        {
            var entries = this.Entries.ToList();
            _Entries.Clear();
            var exits = this.Exits.ToList();
            _Exits.Clear();

            AverageEntryPrice = 0;
            AverageExitPrice = 0;
            AmountInvested = 0;
            QuoteAmountInvested = 0;
            AmountLiquidated = 0;
            QuoteAmountLiquidated = 0;
            AmountRemaining = 0;
            QuoteAmountRemaining = 0;
            foreach (var trade in entries.Concat(exits))
                this.AddTrade(trade);
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
            _IsChanged = true;
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
                this.SetChanged();
                Resume();
                OnNewTrade?.Invoke(this, entry);
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
                this.SetChanged();
                Resume();
                OnNewTrade?.Invoke(this, exit);
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
                this.SetChanged();
                OnClosing?.Invoke(this);
            }
        }

        public void Close()
        {
            this.IsClosed = true;
            this.SetChanged();
            OnClosed?.Invoke(this);
        }

        public void Resume()
        {
            if (IsClosing)
            {
                IsClosing = false;
                CloseDeadTime = DateTime.MaxValue;
                this.SetChanged();
                OnResumed?.Invoke(this);
            }
        }

        public void AcceptChanges()
        {
            _IsChanged = false;
        }

        private void SetChanged()
        {
            _IsChanged = true;
        }



        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }
        class EntriesEqualityComparer : IEqualityComparer<ITrade>, IEqualityComparer<IOrder>
        {
            public bool Equals(ITrade x, ITrade y) => x.Id == y.Id;

            public int GetHashCode(ITrade obj) => obj.GetHashCode();

            public bool Equals(IOrder x, IOrder y) => x.Id == y.Id;

            public int GetHashCode(IOrder obj) => obj.GetHashCode();

            public static EntriesEqualityComparer Instance { get; } = new EntriesEqualityComparer();
        }
    }
}
