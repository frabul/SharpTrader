using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            public bool MarginTrading { get; set; } = false;
            public bool LiquidationOnly { get; set; } = false;
        }
        class NonVolatileVars
        {
            public long TotalOperations { get; set; }
            public long TotalSignals { get; set; }
            public bool EntriesSuspendedByUser { get; set; } = true;
            public DateTime LastUpdate { get; set; }
        }

        //todo the main components should be allowed to be set only during Initialize 
        private NonVolatileVars State = new NonVolatileVars();
        private Dictionary<string, SymbolData> _SymbolsData = new Dictionary<string, SymbolData>();
        private TimeSlice WorkingSlice = new TimeSlice();
        private TimeSlice OldSlice = new TimeSlice();
        private HashSet<Operation> _ActiveOperations = new HashSet<Operation>();
        private HashSet<Operation> _ClosedOperations = new HashSet<Operation>();
        private Serilog.ILogger SymbolsFilterLogger;
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
        public DateTime Time => Market.Time;
        public DateTime NextUpdateTime { get; private set; } = DateTime.MinValue;
        public TimeSpan Resolution { get; set; } = TimeSpan.FromSeconds(10);
        public bool DoMarginTrading { get; private set; }
        public bool EntriesSuspended => State.EntriesSuspendedByUser || EntriesStopppedByStrategy || Config.LiquidationOnly;
        public bool IsPlottingEnabled { get; set; } = false;
        public Serilog.ILogger Logger { get; private set; }

        public TradingAlgo(IMarketApi marketApi, Serilog.ILogger logger, Configuration config)
        {
            Config = config;
            Market = marketApi;
            Market.OnNewTrade += Market_OnNewTrade;
            this.DoMarginTrading = config.MarginTrading;
            Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Destructure.ByTransforming<Signal>(s => new { s.Id, Symbol = s.Symbol.Key, s.Kind, s.EntryExpiry, s.ExpireDate, s.PriceEntry, s.PriceTarget, s.ModifyTime, OpId = s.Operation?.Id })
                .Destructure.ByTransforming<Operation>(op =>
                            new
                            {
                                Symbol = op.Symbol.Key,
                                op.Id,
                                op.IsClosing,
                                op.IsClosed,
                                op.Signal,
                                op.AmountTarget,
                                op.AverageEntryPrice,
                                op.AverageExitPrice,
                                op.QuoteAmountInvested,
                                op.QuoteAmountLiquidated,
                                op.QuoteAmountRemaining,
                                op.Type,
                                op.CloseDeadTime,
                                op.LastInvestmentTime,
                                Entries = op.Entries.Select(t => t.Id).ToArray(),
                                Exits = op.Exits.Select(t => t.Id).ToArray()
                            })
                .Enrich.With(new MarketTimeEnricher(marketApi))
                .Enrich.WithProperty("AlgoName", this.Name)
                .WriteTo.Logger(logger)
                .CreateLogger()
                .ForContext<TradingAlgo>();

            SymbolsFilterLogger = Logger.ForContext("SourceContext", "TradingAlgo.SymbolsFilter");
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
                var req = await Market.GetLastTradesAsync(Market.Time - TimeSpan.FromHours(12));
                if (req.IsSuccessful)
                {
                    foreach (var trade in req.Result)
                        if (SymbolsData.ContainsKey(trade.Symbol))
                            this.WorkingSlice.Add(SymbolsData[trade.Symbol].Symbol, trade);
                        else
                            Logger.Error("Symbol data not found for trade during initialize. {@Trade}", trade);
                }
                else
                {
                    Logger.Error("GetLastTrades failed during initialize.");
                }
            }
            //call on initialize
            await this.OnInitialize();
        }

        protected abstract Task OnInitialize();

        public abstract Task OnUpdate(TimeSlice slice);

        public override async Task OnStartAsync()
        {
            Logger.Information("Initializing.");
            await this.Initialize();
            Logger.Information("Initializing SymbolsFilter.");
            await SymbolsFilter.Initialize(this);
            Logger.Information("Initializing sentry.");
            if (Sentry != null)
                await Sentry.Initialize(this);
            Logger.Information("Initializing allocator.");
            if (Allocator != null)
                await Allocator.Initialize(this);
            Logger.Information("Initializing Executor.");
            if (Executor != null)
                await Executor.Initialize(this);
            Logger.Information("Initializing RiskManager.");
            if (RiskManager != null)
                await RiskManager.Initialize(this);
        }

        public async Task Update(TimeSlice slice)
        {
            //update selected symbols
            var changes = await SymbolsFilter.UpdateAsync(slice);
            if (changes != SelectedSymbolsChanges.None)
            {
                var added_str = string.Join(", ", changes.AddedSymbols.Select(s => s.Key));
                var removed_str = string.Join(", ", changes.RemovedSymbols.Select(s => s.Key));
                var all_selected = string.Join(", ", SymbolsFilter.SymbolsSelected.Select(s => s.Key));

                SymbolsFilterLogger
                    .ForContext("Selected", SymbolsFilter.SymbolsSelected, true)
                    .ForContext("Added", changes.AddedSymbols, true)
                    .ForContext("Removed", changes.RemovedSymbols, true)
                    .Information("Changes in symbols selected.");

                var selectedForOperationsActive = this.ActiveOperations.Select(ao => ao.Symbol).GroupBy(ao => ao.Key).Select(g => g.First()).ToList();
                //release feeds of unused symbols 
                foreach (var sym in changes.RemovedSymbols)
                {
                    SymbolData symbolData = GetSymbolData(sym);
                    symbolData.IsSelectedForTrading = false;
                    //if it doesn't have acrive operations
                    if (!selectedForOperationsActive.Any(aos => aos.Key == sym.Key))
                    {
                        if (symbolData.Feed != null)
                        {
                            symbolData.Feed.OnData -= Feed_OnData;
                            this.ReleaseFeed(symbolData.Feed);
                            symbolData.Feed = null;
                        }
                    }
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
            foreach (ITrade trade in slice.Trades)
            {
                var logger = Logger.ForContext("Symbol", trade.Symbol);
                //first search in active operations
                Operation activeOp = null;
                if (_SymbolsData.ContainsKey(trade.Symbol))
                {
                    var symData = _SymbolsData[trade.Symbol];
                    activeOp = symData.ActiveOperations.FirstOrDefault(op => op.IsTradeAssociated(trade));
                }

                if (activeOp != null)
                {
                    if (activeOp.AddTrade(trade))
                        logger.Information("Trade {TradeId} from {ClientOrderId} added to operation {OperationId}.", trade.Id, trade.ClientOrderId, activeOp.Id);
                }
                else
                {
                    //let's search in closed operations  
                    var opId = Operation.GetOperationIdFromClientOrderId(trade.ClientOrderId);
                    var oldOp = DbClosedOperations.FindById(opId);
                    if (oldOp != null)
                    {
                        if (oldOp.AddTrade(trade))
                        {
                            logger.Warning("Trade {TradeId}  from {ClientOrderId} added to 'old' operation {OperationId}.", trade.Id, trade.ClientOrderId, oldOp.Id);
                            DbClosedOperations.Upsert(oldOp);
                        }
                        //check if it got resumed by this new trade
                        if (!oldOp.IsClosed)
                        {
                            logger.Information("Resuming 'old' operation {OperationId}.", oldOp.Id);
                            this.ResumeOperation(oldOp);
                        }
                        else
                            oldOp.Dispose(); //otherwise we must dispose it
                    }
                    else
                        logger.Warning("The trade {TradeId} from {ClientOrderId} was not associated to any operation.", trade.Id, trade.ClientOrderId);
                }
            }

            // call OnUpdate
            await OnUpdate(slice);

            // get signals 
            if (Sentry != null)
                await Sentry.UpdateAsync(slice);

            //create operations
            if (Allocator != null)
                Allocator.Update(slice);

            //close operations that have been in close queue for enough time
            lock (DbLock)
            {
                this.Db?.BeginTrans();
                List<Operation> operationsToClose = _ActiveOperations.Where(op => this.Time >= op.CloseDeadTime).ToList();
                foreach (var op in operationsToClose)
                {
                    if (op.AmountInvested != 0)
                    {
                        var gain = op.CalculateGainAsQuteAsset(0.001m);
                        var gainPrc = gain * 100 / op.QuoteAmountInvested;
                        Logger
                            .ForContext("Symbol", op.Symbol)
                            .ForContext("Operation", op, true)
                            .Information("{OperationId} - Closing operation - Gain: {gain:f6}, Gain%: {gainPrc:f2}.", op.Id, gain, gainPrc);
                    }
                    else
                        Logger.ForContext("Symbol", op.Symbol).Debug("{OperationId} - Closing operation.", op.Id);

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
                            op.AcceptChanges();
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
                    Logger.Error("Error while running command: {Message}.", ex.Message);
                }
            }

            State.LastUpdate = Market.Time;
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
            Logger.Information("Resuming operation {OperationId} / {Symbol}.", op.Id, op.Symbol);
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

        private SymbolData GetSymbolData(ISymbolInfo sym)
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
                WorkingSlice.Add(Market.GetSymbolInfo(trade.Symbol), trade);
        }

        public Task<ISymbolFeed> GetSymbolFeed(string symbolKey)
        {
            return Market.GetSymbolFeedAsync(symbolKey);
        }
        private volatile bool OnTickRunning;
        private DateTime LastWarningTime;

        public override Task OnTickAsync()
        {
           
            if (!BackTesting && OnTickRunning && Time >= NextUpdateTime)
            {
                if (Time > LastWarningTime + TimeSpan.FromSeconds(30))
                {
                    Logger.Warning("TradingAlgo.OnTickAsync is taking longer than expected: {Elapsed} vs {Expected}."
                                    ,Time - (NextUpdateTime - Resolution), Resolution);
                    LastWarningTime = Time;
                }
            }

            if (!OnTickRunning && Time >= NextUpdateTime)
            {
                try
                {
                    OnTickRunning = true;
                    NextUpdateTime = Time + Resolution;
                    TimeSlice curSlice;
                    lock (WorkingSliceLock)
                    { 
                        curSlice = WorkingSlice;
                        WorkingSlice = OldSlice;
                        WorkingSlice.Clear(Market.Time);

                        OldSlice = curSlice;
                    }
                    return this.Update(curSlice);
                }
                catch { throw; }
                finally { OnTickRunning = false; }
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
            var operationsToFlush = _ClosedOperations
                .Where(op => op.CloseDeadTime < timeLimit && !op.IsChanged)
                .ToArray();
            if (operationsToFlush.Length > 0)
            {
                Logger.Debug("Flushing {Cnt} operations.", operationsToFlush.Length);
                foreach (var op in operationsToFlush)
                {
                    _ClosedOperations.Remove(op);
                    op.Dispose();
                }

            }
        }
    }

}
