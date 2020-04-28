using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public class SymbolData
    {
        /// <summary>
        /// Feed is automatcly initialized by the algo for the symbols requested by the symbols selector module
        /// </summary>
        public ISymbolFeed Feed { get; set; }

        public List<Operation> ActiveOperations { get; set; } = new List<Operation>();
        public List<Operation> ClosedOperations { get; set; } = new List<Operation>();

        /// <summary>
        /// This property can be used by sentry module to store its data
        /// </summary>
        public object SentryData { get; set; }
        public object OperationsManagerData { get; set; }
        public object AllocatorData { get; set; }
        public SymbolInfo Symbol => Feed.Symbol;

        public object RiskManagerData { get; set; }
    }

    public abstract class TradingAlgo : TraderBot
    { 
        //todo the main components should be allowed to be set only during Initialize 
        private Dictionary<string, SymbolData> _SymbolsData = new Dictionary<string, SymbolData>();
        private TimeSlice WorkingSlice = new TimeSlice();
        private TimeSlice OldSlice = new TimeSlice();
        private List<Operation> _ActiveOperations = new List<Operation>();
        private NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public Action<PlotHelper> ShowPlotCallback { get; set; }
        public IReadOnlyDictionary<string, SymbolData> SymbolsData => _SymbolsData;

        public IReadOnlyList<Operation> ActiveOperations => _ActiveOperations;

        public SymbolsSelector SymbolsFilter { get; set; }
        public Sentry Sentry { get; set; }
        public FundsAllocator Allocator { get; set; }
        public OperationManager Executor { get; set; }
        public RiskManager RiskManager { get; set; }
        public IMarketApi Market { get; }
        public DateTime LastUpdate { get; set; }

        public DateTime Time => Market.Time;
        public DateTime NextUpdateTime { get; private set; } = DateTime.MinValue;
        public TimeSpan Resolution { get; set; } = TimeSpan.FromMinutes(1);
        public bool IsTradingStopped { get; private set; } = false;
        public object Name { get; set; }
        public bool IsPlottingEnabled { get; set; } = false;

        public TradingAlgo(IMarketApi marketApi)
        {
            Market = marketApi;
            Market.OnNewTrade += Market_OnNewTrade;
        }

        public abstract Task Initialize();

        public abstract Task OnUpdate(TimeSlice slice);

        public override async Task OnStartAsync()
        {
            await this.Initialize();
            await SymbolsFilter.Initialize(this);
            if (Sentry != null)
                await Sentry.Initialize(this);
            if (Allocator != null)
                await Allocator.Initialize(this);
            if (Executor != null)
                await Executor.Initialize(this);
            if (RiskManager != null)
                await RiskManager.Initialize(this);
        }

        private void AddNewOperation(Operation op)
        {
            this._ActiveOperations.Add(op);
            _SymbolsData[op.Symbol.Key].ActiveOperations.Add(op);
        }

        public async Task Update(TimeSlice slice)
        {
            LastUpdate = Market.Time;

            //update selected symbols
            var changes = await SymbolsFilter.UpdateAsync(slice);
            if (changes != SelectedSymbolsChanges.None)
            {
                if (Sentry != null)
                    Sentry.OnSymbolsChanged(changes);

                if (Allocator != null)
                    Allocator.OnSymbolsChanged(changes);


                if (Executor != null)
                    Executor.OnSymbolsChanged(changes);

                if (RiskManager != null)
                    RiskManager.OnSymbolsChanged(changes);

                //release feeds of unused symbols
                foreach (var sym in changes.RemovedSymbols)
                {
                    SymbolData symbolData = _SymbolsData[sym.Key];
                    this.ReleaseFeed(symbolData.Feed);
                    symbolData.Feed = null;
                }

                //add feeds for added symbols
                foreach (var sym in changes.AddedSymbols)
                {
                    SymbolData symbolData;
                    if (!_SymbolsData.TryGetValue(sym.Key, out symbolData))
                    {
                        symbolData = new SymbolData();
                        _SymbolsData.Add(sym.Key, symbolData);
                    }

                    symbolData.Feed = await this.GetSymbolFeed(sym.Key);
                    symbolData.Feed.OnData -= Feed_OnData;
                    symbolData.Feed.OnData += Feed_OnData;
                }
            }

            // register trades with their linked operations
            foreach (ITrade trade in slice.Trades)
            {
                //first search in active operations
                var activeOp = _SymbolsData[trade.Symbol].ActiveOperations.FirstOrDefault(op => op.IsTradeAssociated(trade));
                if (activeOp != null)
                {
                    activeOp.AddTrade(trade);
                    Logger.Info($"New trade {trade} added top operation {activeOp}");
                }
                else
                {
                    Logger.Info($"New trade {trade} without any associated operation");
                }

            }

            await OnUpdate(slice);

            // get signals 
            if (Sentry != null)
                Sentry.UpdateAsync(slice);

            //create operations
            if (Allocator != null)
                Allocator.Update(slice);

            //close operations that have been in close queue for enough time
            for (int i = 0; i < this.ActiveOperations.Count; i++)
            {
                var op = this.ActiveOperations[i];
                if (this.Time >= op.CloseDeadTime)
                {
                    op.Close();
                    //move closed operations
                    this.SymbolsData[op.Symbol.Key].ActiveOperations.Remove(op);
                    this._ActiveOperations.RemoveAt(i--);
                    //if there wasn't any transaction for the operation then we just forget it
                    if (op.AmountInvested > 0)
                        this.SymbolsData[op.Symbol.Key].ClosedOperations.Add(op);
                }
            }

            //add new operations that have been created 
            foreach (var op in slice.NewOperations)
                this.AddNewOperation(op);

            //manage orders
            if (Executor != null)
                await Executor.Update(slice);

            //manage risk
            if (RiskManager != null)
                await RiskManager.Update(slice);
        }

        public void ShowPlot(PlotHelper plot)
        {
            ShowPlotCallback?.Invoke(plot);
        }

        public void ForceCloseOperation(string id)
        {
            throw new NotImplementedException();
        }

        public Task LiquidateOperation(string v)
        {
            throw new NotImplementedException();
        }

        private void Feed_OnData(ISymbolFeed symFeed, IBaseData dataRecord)
        {
            lock (WorkingSlice)
                WorkingSlice.Add(symFeed.Symbol, dataRecord);
        }

        private void Market_OnNewTrade(IMarketApi market, ITrade trade)
        {
            lock (WorkingSlice)
                WorkingSlice.Add(SymbolsData[trade.Symbol].Symbol, trade);
        }

        public Task Stop()
        {
            throw new NotImplementedException();
        }

        public Task<ISymbolFeed> GetSymbolFeed(string symbolKey)
        {
            return Market.GetSymbolFeedAsync(symbolKey);
        }

        public override Task OnTickAsync()
        {
            if (Time >= NextUpdateTime)
            {
                TimeSlice curSlice;
                lock (WorkingSlice)
                {
                    NextUpdateTime = Time + Resolution;

                    curSlice = WorkingSlice;
                    WorkingSlice = OldSlice;
                    WorkingSlice.Clear(Market.Time);

                    OldSlice = curSlice;
                }
                return this.Update(curSlice);
            }
            else
                return Task.CompletedTask;
        }

        public void ReleaseFeed(ISymbolFeed feed)
        {
            Market.DisposeFeed(feed);
        }

        public void ResumeEntries()
        {
            IsTradingStopped = false;
        }

        public Task StopEntries()
        {
            IsTradingStopped = true;
            //close all entry orders
            return this.Executor.CancelEntryOrders();
        }
    }

}
