using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SharpTrader.AlgoFramework
{
    public enum OperationType
    {
        BuyThenSell,
        SellThenBuy,
    }

    public class Operation
    {
        //todo prevedere la possibile chiusura di una operazione passando i fondi ad un'altra 
        private int OrdersCount = 0;
        private Signal _Signal;

        private HashSet<ITrade> _Entries = new HashSet<ITrade>();
        private HashSet<ITrade> _Exits = new HashSet<ITrade>();

        public event Action<Operation, ITrade> OnNewTrade;
        public event Action<Operation> OnClosing;
        public event Action<Operation> OnClosed;
        public event Action<Operation> OnResumed;
        /// <summary>
        /// All trades associated with thos operation
        /// </summary>
        public IEnumerable<ITrade> AllTrades => _Entries.Concat(Exits);
        /// <summary>
        /// Trades that were meant as entries
        /// </summary>
        public IEnumerable<ITrade> Entries => _Entries;
        /// <summary>
        /// Trades that were meant as exits
        /// </summary>
        public IEnumerable<ITrade> Exits => _Exits;

        /// <summary>
        /// Unique identifier for this operation
        /// </summary>
        public string Id { get; private set; }
        public DateTime CreationTime { get; private set; }

        public object ExecutorData { get; set; }

        /// <summary>
        /// Insight associated with the operation
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

        public AssetSum AmountTarget { get; private set; }
        public decimal AverageEntryPrice { get; private set; }
        public decimal AverageExitPrice { get; private set; }
        public decimal AmountInvested { get; private set; }
        public decimal QuoteAmountInvested { get; private set; }
        public decimal AmountLiquidated { get; private set; }
        public decimal QuoteAmountLiquidated { get; private set; }
        public decimal AmountRemaining { get; private set; }
        public decimal QuoteAmountRemaining { get; private set; }

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

        internal bool IsEntryExpired(DateTime time)
        {
            return time >= this.Signal.EntryExpiry;
        }

        internal bool IsExitExpired(DateTime time)
        {
            return time >= this.Signal.ExpireDate;
        }

        public OperationType Type { get; internal set; }
        public bool EntryFulfilled { get; internal set; }
        public SymbolInfo Symbol => Signal.Symbol;

        public bool RiskManaged { get; internal set; }
        public object RiskManagerData { get; internal set; }
        public TradeDirection EntryTradeDirection { get; internal set; }
        public TradeDirection ExitTradeDirection { get; internal set; }
        public bool IsClosed { get; private set; }
        public bool IsClosing { get; private set; }
        public DateTime CloseDeadTime { get; private set; } = DateTime.MaxValue;
        public DateTime LastInvestmentTime { get; private set; }

        public Operation(string id)
        {
            Id = id;
        }

        public Operation(string id, Signal signal, AssetSum assetSum, OperationType operType)
        {
            this.Id = id;
            this.Signal = signal;
            this.AmountTarget = assetSum;
            this.Type = operType;
            this.CreationTime = signal.CreationTime;
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

    }
}
