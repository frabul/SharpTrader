using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.AlgoFramework
{
    public abstract partial class TradingAlgo : TraderBot
    {
        public class Configuration
        {
            public string DataDir { get; set; } = Path.Combine(".", "Data");
            public string Name { get; set; } = "Unnamed v1";
            public bool SaveData { get; set; } = false;
        }
        class NonVolatileVars
        {
            public long TotalOperations { get; set; }
            public long TotalSignals { get; set; }
            public bool EntriesSuspendedByUser { get; set; }
        }

        //todo the main components should be allowed to be set only during Initialize 
        private NonVolatileVars State = new NonVolatileVars();
        private Dictionary<string, SymbolData> _SymbolsData = new Dictionary<string, SymbolData>();
        private TimeSlice WorkingSlice = new TimeSlice();
        private TimeSlice OldSlice = new TimeSlice();
        private HashSet<Operation> _ActiveOperations = new HashSet<Operation>();
        private HashSet<Operation> _ClosedOperations = new HashSet<Operation>();
        private NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private Configuration Config;
        private object WorkingSliceLock = new object();
        private bool EntriesStopppedByStrategy = false;
        public abstract string Version { get; }
        public string Name => Config.Name;
        public string MyDataDir => Path.Combine(Config.DataDir, Config.Name);
        public Action<PlotHelper> ShowPlotCallback { get; set; }
        public IReadOnlyDictionary<string, SymbolData> SymbolsData => _SymbolsData;
        public IReadOnlyCollection<Operation> ActiveOperations => _ActiveOperations;
        public IReadOnlyCollection<Operation> ClosedOperations => _ClosedOperations;
        public SymbolsSelector SymbolsFilter { get; set; }
        public Sentry Sentry { get; set; }
        public FundsAllocator Allocator { get; set; }
        public OperationManager Executor { get; set; }
        public RiskManager RiskManager { get; set; }
        public IMarketApi Market { get; }
        public DateTime LastUpdate { get; set; }
        public DateTime Time => Market.Time;
        public DateTime NextUpdateTime { get; private set; } = DateTime.MinValue;
        public TimeSpan Resolution { get; set; } = TimeSpan.FromSeconds(10);

        public bool EntriesSuspended => State.EntriesSuspendedByUser || EntriesStopppedByStrategy;
        public bool IsPlottingEnabled { get; set; } = false;

        public TradingAlgo(IMarketApi marketApi, Configuration config)
        {
            Config = config;
            Market = marketApi;
            Market.OnNewTrade += Market_OnNewTrade;
        }

        public async Task Initialize()
        {
            //load saved state
            if (Config.SaveData)
            {
                this.ConfigureSerialization();
                this.LoadNonVolatileVars();
            }
            if (!BackTesting)
            {
                //on restart there is the possibility that we missed some trades, let's reload the last trades
                var req = await Market.GetLastTradesAsync(Market.Time - TimeSpan.FromHours(6));
                if (req.IsSuccessful)
                {
                    foreach (var trade in req.Result)
                        if (SymbolsData.ContainsKey(trade.Symbol))
                            this.WorkingSlice.Add(SymbolsData[trade.Symbol].Symbol, trade);
                        else
                            Logger.Error($"{Name} - Symbol data not found for trade {trade} during initialize.");
                }
                else
                {
                    Logger.Error("{0} - GetLastTrades failed during initialize", Name);
                }
            }
            //call on initialize
            await this.OnInitialize();
        }

        protected abstract Task OnInitialize();

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

        public async Task Update(TimeSlice slice)
        {
            LastUpdate = Market.Time;
            //update selected symbols
            var changes = await SymbolsFilter.UpdateAsync(slice);
            if (changes != SelectedSymbolsChanges.None)
            {
                var selectedForOperationsActive = this.ActiveOperations.Select(ao => ao.Symbol).GroupBy(ao => ao.Key).Select(g => g.First()).ToList();
                //release feeds of unused symbols
                var removedSymbolsWithoutActiveOperations = changes.RemovedSymbols.Where(rs => !selectedForOperationsActive.Any(aos => aos.Key == rs.Key));
                foreach (var sym in removedSymbolsWithoutActiveOperations)
                {
                    SymbolData symbolData = GetSymbolData(sym);
                    symbolData.IsSelectedForTrading = false;
                    symbolData.Feed.OnData -= Feed_OnData;
                    this.ReleaseFeed(symbolData.Feed);
                    symbolData.Feed = null;
                }

                //add feeds for added symbols and those that have open operations
                foreach (var sym in changes.AddedSymbols)
                {
                    SymbolData symbolData = GetSymbolData(sym);
                    symbolData.IsSelectedForTrading = true;

                }
                foreach (var sym in changes.AddedSymbols.Concat(selectedForOperationsActive))
                {
                    SymbolData symbolData = GetSymbolData(sym);
                    if (symbolData.Feed == null)
                    {
                        symbolData.Feed = await this.GetSymbolFeed(sym.Key);
                        symbolData.Feed.OnData -= Feed_OnData;
                        symbolData.Feed.OnData += Feed_OnData;
                    }
                }

                if (Sentry != null)
                    await Sentry.OnSymbolsChanged(changes);

                if (Allocator != null)
                    Allocator.OnSymbolsChanged(changes);

                if (Executor != null)
                    Executor.OnSymbolsChanged(changes);

                if (RiskManager != null)
                    RiskManager.OnSymbolsChanged(changes);
            }

            // register trades with their linked operations
            Operation[] oldOperations = null;
            foreach (ITrade trade in slice.Trades)
            {
                //first search in active operations
                var symData = _SymbolsData[trade.Symbol];
                var activeOp = symData.ActiveOperations.FirstOrDefault(op => op.IsTradeAssociated(trade));
                if (activeOp != null)
                {
                    activeOp.AddTrade(trade);
                    Logger.Info($"{Time} - New trade for operation {activeOp.ToString()}: {trade.ToString()}");
                }
                else
                {
                    lock (DbLock)
                    {
                        //let's search in closed operations
                        oldOperations = oldOperations ?? DbClosedOperations.Find(o => o.CreationTime >= Market.Time.AddDays(-5)).ToArray();
                    }
                    var oldOp = oldOperations.FirstOrDefault(op => op.IsTradeAssociated(trade));
                    //dispose the operations that we dont't need anymore
                    foreach (var oper in oldOperations.Where(oo => oo != oldOp))
                        oper.Dispose();
                      
                    if (oldOp != null)
                    {
                        Logger.Info($"{Time} - New trade for 'old' operation {activeOp}: {trade.ToString()}");
                        oldOp.AddTrade(trade);
                        //check if it got resumed by this new trade
                        if (!oldOp.IsClosed)
                        {
                            this.ResumeOperation(oldOp);
                            Logger.Info($"Resuming 'old' operation {activeOp}.");
                        }
                    }
                    else
                        Logger.Trace($"{Time} - New trade {trade.ToString()} without any associated operation");

                }
            }
            await OnUpdate(slice);

            // get signals 
            if (Sentry != null)
                Sentry.UpdateAsync(slice);

            //create operations
            if (Allocator != null)
                Allocator.Update(slice);
            lock (DbLock)
            {
                this.Db?.BeginTrans();
                //close operations that have been in close queue for enough time
                List<Operation> operationsToClose = _ActiveOperations.Where(op => this.Time >= op.CloseDeadTime).ToList();
                foreach (var op in operationsToClose)
                {
                    if(op.AmountInvested > 0)
                        Logger.Info("{0} - Closing operation {1}.", Time, op.ToString("c"));
                    else
                        Logger.Debug("{0} - Closing operation {1}.", Time, op.ToString("c"));
                    op.Close();
                    _ActiveOperations.Remove(op);
                    if (this.Config.SaveData || op.AmountInvested > 0)
                        this._ClosedOperations.Add(op);
                    this.SymbolsData[op.Symbol.Key].CloseOperation(op);

                    if (this.Config.SaveData)
                        lock (DbLock)
                        {
                            //update database 
                            DbActiveOperations.Delete(op.Id);
                            DbClosedOperations.Upsert(op);
                        }
                }
                this.Db?.Commit();
            }

            //add new operations that have been created 
            foreach (var op in slice.NewOperations)
                AddActiveOperation(op);

            //manage orders
            if (Executor != null)
                await Executor.Update(slice);

            //manage risk
            if (RiskManager != null)
                await RiskManager.Update(slice);

            while (Commands.Count > 0)
            {
                try
                {
                    if (Commands.TryDequeue(out Command command))
                        await command.Run();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error while running command: {ex.Message}.");
                }
            }

            if (Config.SaveData)
            {
                SaveNonVolatileVars();
            }
        }

        private void AddActiveOperation(Operation op)
        {
            this._ActiveOperations.Add(op);
            var symData = GetSymbolData(op.Symbol);
            symData.AddActiveOperation(op);
            if (this.Config.SaveData)
                lock (DbLock)
                    DbActiveOperations.Upsert(op);
        }

        private void ResumeOperation(Operation op)
        {
            Logger.Info($"Resuming operation {op}.");
            op.Resume();
            AddActiveOperation(op);
            var removed = this._ClosedOperations.Remove(op);
            if (this.Config.SaveData)
                lock (DbLock)
                    DbClosedOperations.Delete(op.Id);
        }

        public string GetNewOperationId()
        {
            if (Name.Length > 6)
                return this.Name.Substring(0, 6) + "_" + (State.TotalOperations++).ToString();
            else
                return $"{this.Name}_{ State.TotalOperations++ }";
        }

        public string GetNewSignalId()
        {
            return (State.TotalSignals++).ToString();
        }

        private SymbolData GetSymbolData(SymbolInfo sym)
        {
            SymbolData symbolData;
            if (!_SymbolsData.TryGetValue(sym.Key, out symbolData))
            {
                symbolData = new SymbolData(sym);
                _SymbolsData.Add(sym.Key, symbolData);
            }
            return symbolData;
        }

        public void ShowPlot(PlotHelper plot)
        {
            ShowPlotCallback?.Invoke(plot);
        }


        private void Feed_OnData(ISymbolFeed symFeed, IBaseData dataRecord)
        {
            //todo bisogna accertartci che i dati che riceviamo sono del giusto timeframe!
            lock (WorkingSliceLock)
                WorkingSlice.Add(symFeed.Symbol, dataRecord);
        }

        private void Market_OnNewTrade(IMarketApi market, ITrade trade)
        {
            lock (WorkingSliceLock)
                WorkingSlice.Add(SymbolsData[trade.Symbol].Symbol, trade);
        }

        public Task<ISymbolFeed> GetSymbolFeed(string symbolKey)
        {
            return Market.GetSymbolFeedAsync(symbolKey);
        }

        public override Task OnTickAsync()
        {
            if (Time >= NextUpdateTime)
            {
                if (!BackTesting)
                {
                    //ClearClosedOperations(TimeSpan.FromHours(1));
                    if (Time - NextUpdateTime > TimeSpan.FromSeconds(Resolution.TotalSeconds * 1.3))
                        Logger.Warn($"{Name} - OnTick duration longer than expected. Expected {Resolution} - real {Time - NextUpdateTime + Resolution }");
                }

                TimeSlice curSlice;
                lock (WorkingSliceLock)
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
            EntriesStopppedByStrategy = false;
        }

        public Task StopEntries()
        {
            EntriesStopppedByStrategy = true;
            //close all entry orders
            return this.Executor.CancelEntryOrders();
        }

        public void ClearClosedOperations(TimeSpan keepRange)
        {
            var timeLimit = Time - keepRange;
            foreach (var op in ClosedOperations.Where(op => op.LastInvestmentTime < timeLimit).ToArray())
            {
                _ClosedOperations.Remove(op);
            }
        }
    }

}
