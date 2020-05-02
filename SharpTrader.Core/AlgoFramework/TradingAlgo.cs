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
    public abstract class TradingAlgo : TraderBot
    {
        public class Configuration
        {
            public string DataDir { get; set; } = Path.Combine(".", "Data");
            public string Name { get; set; } = "Unnamed";
            public bool SaveData { get; set; } = false;
        }
        class NonVolatileVars
        {
            public long TotalOperations { get; set; }
            public long TotalSignals { get; set; }
        }

        //todo the main components should be allowed to be set only during Initialize 
        private Dictionary<string, SymbolData> _SymbolsData = new Dictionary<string, SymbolData>();
        private TimeSlice WorkingSlice = new TimeSlice();
        private TimeSlice OldSlice = new TimeSlice();
        private List<Operation> _ActiveOperations = new List<Operation>();
        private NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private Configuration Config;
        private LiteDatabase Db; 
        private NonVolatileVars State = new NonVolatileVars();

        public string Name => Config.Name;
        public string MyDataDir => Path.Combine(Config.DataDir, Config.Name);
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
                ReloadSavedState();

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

        private void AddNewOperation(Operation op)
        {
            this._ActiveOperations.Add(op);
            var symData = GetSymbolData(op.Symbol);
            symData.ActiveOperations.Add(op);
            this.Db?.GetCollection<Operation>("ActiveOperations").Upsert(op);
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
                    symbolData.Feed.OnData -= Feed_OnData;
                    this.ReleaseFeed(symbolData.Feed);
                    symbolData.Feed = null;
                }

                //add feeds for added symbols
                foreach (var sym in changes.AddedSymbols)
                {
                    SymbolData symbolData = GetSymbolData(sym);

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

            this.Db?.BeginTrans();
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

                    //update database
                    this.Db?.GetCollection("ActiveOperations").Delete(op.Id);
                    this.Db?.GetCollection<Operation>("ClosedOperations").Upsert(op);


                    //if there wasn't any transaction for the operation then we just forget it
                    if (op.AmountInvested > 0)
                        this.SymbolsData[op.Symbol.Key].ClosedOperations.Add(op);
                }
            }
            this.Db?.Commit();

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


        public string GetNewOperationId()
        {
            return (State.TotalOperations++).ToString();
        }

        public string GetNewSignalId()
        {
            return (State.TotalSignals++).ToString();
        }

        public async Task Stop()
        {
            await this.StopEntries();
            foreach (var op in this.ActiveOperations)
            {
                await this.Executor.CancelAllOrders(op);
                await this.RiskManager.CancelAllOrders(op);
            }
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
        public void LoadState()
        {

        }
        public void SaveState()
        {

        }

        private List<Signal> SignalsDeserialized = new List<Signal>();
        public void ReloadSavedState()
        {
            var dbPath = Path.Combine(MyDataDir, "MyData.db");
            if (!Directory.Exists(MyDataDir))
                Directory.CreateDirectory(MyDataDir);
            this.Db = new LiteDatabase(dbPath);

            BsonMapper mapper = BsonMapper.Global;
            BsonMapper defaultMapper = new BsonMapper();
            //todo register mapper for custom components
            Market.RegisterSerializationHandlers(mapper);
            this.Executor?.RegisterSerializationHandlers(mapper);
            this.RiskManager?.RegisterSerializationHandlers(mapper);
            this.Sentry?.RegisterSerializationHandlers(mapper);

            //register symbol info mapper
            mapper.RegisterType<SymbolInfo>(o => defaultMapper.Serialize(o.Key), bson => Market.GetSymbols().FirstOrDefault(s => s.Key == bson.AsString));

            //---- add mapper for signals
            //this mappers 
            BsonValue serializeSignal(Signal signal) => defaultMapper.Serialize(signal); 
            Signal deserializeSignal(BsonValue bson){
                var signal = SignalsDeserialized.FirstOrDefault(s => s.Id == bson["Id"].AsString);
                if (signal == null)
                    signal = defaultMapper.Deserialize<Signal>(bson);
                return signal;
            }
            mapper.RegisterType<Signal>(serializeSignal, deserializeSignal);

            //---- add mapper for operations
            BsonValue SerializeOp(Operation op)
            {
                var document = defaultMapper.ToDocument(op);
                document["AllTrades"] = new BsonArray(op.AllTrades.Select(tr => mapper.Serialize(tr)));
                return document;
            }
            Operation DeserializeOp(BsonValue bson)
            {
                var op = defaultMapper.Deserialize<Operation>(bson);
                var allTrades = mapper.Deserialize<object[]>(bson["AllTrades"]);
                foreach (var trade in allTrades)
                    op.AddTrade(trade as ITrade);
                return op;
            }
            mapper.RegisterType<Operation>(SerializeOp, DeserializeOp);

            //rebuild symbols data
            foreach (var symData in Db.GetCollection<SymbolData>("SymbolsData").FindAll())
                _SymbolsData[symData.Id] = symData;
            var closedOperations = Db.GetCollection<Operation>("ClosedOperations").FindAll().ToArray();
            //rebuild operations 
            //closed operations are not loaded in current session behind
            var activeOperations = Db.GetCollection<Operation>("ActiveOperations");
            foreach (var op in activeOperations.FindAll().ToArray())
            {
                var symData = GetSymbolData(op.Symbol);
                symData.ActiveOperations.Add(op);
            }
        }

        protected virtual object GetState() { return new object(); }
        protected virtual void RestoreState(object state) { }

        public void SaveNonVolatileVars()
        {
            //save my internal state
            BsonDocument states = new BsonDocument();
            states["_id"] = "TradingAlgoState"; 
            states["State"] = Db.Mapper.Serialize(State);
            
            //Save derived state  
            states["DerivedClassState"] = Db.Mapper.Serialize(GetState()); 

            //save module states 
            states["Sentry"] = Db.Mapper.Serialize(Sentry.GetState());
            states["Allocator"] = Db.Mapper.Serialize(Allocator.GetState()); 
            states["Executor"] = Db.Mapper.Serialize(Executor.GetState());
            states["RiskManager"] = Db.Mapper.Serialize(RiskManager.GetState());

            Db.GetCollection("State").Upsert(states);
        }

        public void LoadNonVolatileVars()
        { 
            //reload my internal state
            BsonDocument states = Db.GetCollection("State").FindById("TradingAlgoState");
            State = Db.Mapper.Deserialize<NonVolatileVars>(states["State"]);
            
            //reload derived class state
            this.RestoreState(Db.Mapper.Deserialize<object>(states["DerivedClassState"]));

            //reload modules state
            Sentry.RestoreState(Db.Mapper.Deserialize<object>(states["Sentry"]));
            Allocator.RestoreState(Db.Mapper.Deserialize<object>(states["Allocator"]));
            Executor.RestoreState(Db.Mapper.Deserialize<object>(states["Executor"]));
            RiskManager.RestoreState(Db.Mapper.Deserialize<object>(states["RiskManager"])); 
        }

        public void SaveDataBase()
        {
            //todo Signals need to be saved and all of theirs reference need to be stored byref
            foreach (var symData in SymbolsData.Values)
                Db.GetCollection<SymbolData>("SymbolsData").Upsert(symData);
            Db.Checkpoint(); 
        }


    }

}
