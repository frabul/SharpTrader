using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;
using System.Diagnostics;
using LiteDB;
using BinanceExchange.API.Models.Response;
using System.Runtime.InteropServices;

namespace SharpTrader.Storage
{
    public enum ChunkFileVersion
    {
        V2,
        V3
    }
    public enum ChunkSpan { OneMonth, OneDay }
    //TODO make thread safe
    public class TradeBarsRepository
    {
        private Serilog.ILogger Logger = Serilog.Log.ForContext<TradeBarsRepository>();
        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        private string DataDir;
        private LiteDatabase Db;
        private ILiteCollection<SymbolHistoryMetaDataInternal> DbSymbolsMetaData;
        private Dictionary<string, SymbolHistoryMetaDataInternal> SymbolsMetaData;
        public ChunkFileVersion ChunkFileVersion { get; private set; }
        public ChunkSpan ChunkSpan { get; private set; }
        public TradeBarsRepository(string dataDir) : this(dataDir, ChunkFileVersion.V3, ChunkSpan.OneDay)
        {

        }
        public TradeBarsRepository(string dataDir, ChunkFileVersion cv, ChunkSpan chunkSpan)
        {
            this.ChunkFileVersion = cv;
            this.ChunkSpan = chunkSpan;
            DataDir = Path.Combine(dataDir, "RatesDB");
            if (!Directory.Exists(DataDir))
                Directory.CreateDirectory(DataDir);

            Init();
        }

        public SymbolHistoryMetaData GetMetaData(SymbolHistoryId historyInfo) => GetMetaDataInternal(historyInfo);
        private SymbolHistoryMetaDataInternal GetMetaDataInternal(SymbolHistoryId historyInfo)
        {
            lock (SymbolsMetaData)
            {
                if (!SymbolsMetaData.TryGetValue(historyInfo.Key, out var metaData))
                {
                    metaData = new SymbolHistoryMetaDataInternal(historyInfo);
                    SymbolsMetaData.Add(historyInfo.Key, metaData);
                    metaData.SetSaveMode(ChunkFileVersion, ChunkSpan);
                }
                return metaData;
            }

        }
        public virtual ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData, DateTime endOfData)
        {
            var meta = GetMetaDataInternal(info);
            meta.LoadHistory(DataDir, startOfData, endOfData);
            return new SymbolHistory(meta.View, startOfData, endOfData);
        }

        public ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData)
        {
            return GetSymbolHistory(info, startOfData, DateTime.MaxValue);
        }

        public ISymbolHistory GetSymbolHistory(SymbolHistoryId info)
        {
            return GetSymbolHistory(info, DateTime.MinValue);
        }

        public void AddCandlesticks(SymbolHistoryId symId, IEnumerable<Candlestick> candles)
        {
            //load all available data in date rage 
            if (candles.Any())
            {
                var meta = GetMetaDataInternal(symId);
                //meta.LoadHistory(DataDir, candles.First().OpenTime, DateTime.MaxValue);
                //add data 
                //Debug.Assert(sdata.Ticks.Count > 0);
                //Debug.Assert(candles.First().OpenTime > sdata.Ticks.First().OpenTime, "Error in sdata times");
                meta.AddBars(candles);
            }
        }

        internal void UpdateFirstKnownData(SymbolHistoryId info, ITradeBar firstAvailable)
        {
            var data = GetMetaDataInternal(info);
        }

        private void Init()
        {
            var connectionString = $"Filename={Path.Combine(this.DataDir, "DatabaseV3.db")};connection=shared";
            Db = new LiteDB.LiteDatabase(connectionString);
            Db.Pragma("UTC_DATE", true);


            DbSymbolsMetaData = Db.GetCollection<SymbolHistoryMetaDataInternal>("SymbolsMetaData");

            if (DbSymbolsMetaData.FindOne(e => true) == null)
            {
                RebuildAllDatabase(DataDir, true);
            }

            SymbolsMetaData = DbSymbolsMetaData.FindAll().ToDictionary(md => md.Id);
            foreach (var item in SymbolsMetaData)
                item.Value.SetSaveMode(ChunkFileVersion, ChunkSpan);

            ValidateIndex();
        }
        private void ValidateIndex()
        {
            IEnumerable<HistoryChunkId> allChunks = DiscoverChunks(DataDir).Values;

            foreach (var symData in SymbolsMetaData.Values.ToArray())
            {
                bool ok = true;
                var chunks = symData.Chunks.ToList();
                symData.Chunks.Clear();
                foreach (var chunk in chunks)
                {
                    bool canAddChunk = true;
                    var newPath = chunk.GetFilePath(DataDir);
                    //check that file exists
                    if (File.Exists(newPath))
                    {

                        var fileInfo = new FileInfo(newPath);
                        //check that file size is plausible
                        if (fileInfo.Length > 3e6)
                        {
                            Logger.Error("Error: history file {FilePath} has anomalous size. It will be deleted", newPath);
                            ok = false;
                            File.Delete(newPath);
                            canAddChunk = false;
                        }
                        //check that id is right
                        if (chunk.HistoryId.Key != symData.HistoryId.Key)
                        {
                            Logger.Error("Error: history file {FilePath} has wrong history id {HistoryId}", newPath, chunk.HistoryId.Key);
                            File.Delete(newPath);
                            canAddChunk = false;
                        }
                    }
                    else
                    {
                        canAddChunk = false;
                        ok = false;
                        Logger.Error("Error: history file {FilePath} does not exist.", newPath);
                    }
                    if (canAddChunk)
                        symData.Chunks.Add(chunk);
                }
                //rebuild history if needed
                if (ok == false)
                {
                    Logger.Warning("Rebuilding corrupted history {@HistoryId}", symData.HistoryId);
                    symData.ClearView();
                    var newSymData = new SymbolHistoryMetaDataInternal(symData.HistoryId);
                    newSymData.SetSaveMode(ChunkFileVersion, ChunkSpan);
                    //load data of all chunks of the symbol for each of them, load file, add data, delete file
                    foreach (var chunk in allChunks.Where(c => c.HistoryId.Key == symData.HistoryId.Key))
                    {
                        var chunkData = HistoryChunk.Load(chunk.GetFilePath(DataDir)).Result;
                        newSymData.AddBars(chunkData.Ticks);
                        File.Delete(chunk.GetFilePath(DataDir));
                    }

                    //save data
                    newSymData.Flush(this.DataDir);

                    //update dictionary
                    SymbolsMetaData[newSymData.Id] = newSymData;

                    //update db
                    DbSymbolsMetaData.Upsert(newSymData);
                    newSymData.Validated = true;
                }
                symData.Validated = true;
            }
        }

        public static Dictionary<string, HistoryChunkId> DiscoverChunks(string dataDir)
        {
            // discover ass chunk files in directory  
            Dictionary<string, HistoryChunkId> allChunks = new Dictionary<string, HistoryChunkId>();
            foreach (var file in Directory.EnumerateFiles(dataDir, "*.*", SearchOption.AllDirectories))
            {
                if (HistoryChunkId.TryParse(file, out var chunkId))
                    allChunks.Add(file, chunkId);
            }
            return allChunks;
        }

        /// <summary>
        /// Loads all files then saves them again ( so format is updates )
        /// </summary>
        private void RebuildAllDatabase(string filesPath, bool deleteLoadedFiles)
        {
            //get a
            var chunks = DiscoverChunks(filesPath);
            var filesGrouped = chunks.GroupBy(c => c.Value.HistoryId.Key).ToList();

            var data = new List<SymbolHistoryMetaDataInternal>();
            Logger.Information("Rebuilding history db, found {GroupCnt} chunks grouped by id, {ChunkFileVersion} {ChunkSpan}",
                filesGrouped.Count,
                ChunkFileVersion,
                ChunkSpan);
            int cnt = 0;
            foreach (var group in filesGrouped)
            {
                Logger.Information("Converting {ConvertedCnt}/{GroupCnt}", cnt, filesGrouped.Count);
                var histMetadata = new SymbolHistoryMetaDataInternal(group.First().Value.HistoryId);
                histMetadata.SetSaveMode(ChunkFileVersion, ChunkSpan);
                foreach (var chunk in group)
                {
                    var chunkFilePath = chunk.Key;
                    var fdata = HistoryChunk.Load(chunkFilePath).Result;
                    histMetadata.AddBars(fdata.Ticks);
                    if (deleteLoadedFiles)
                        File.Delete(chunkFilePath);
                }
                histMetadata.Flush(DataDir);
                data.Add(histMetadata);
                cnt++;
            }
            //reinsert everything
            DbSymbolsMetaData.DeleteAll();
            Db.Checkpoint();
            foreach (var dat in data)
                DbSymbolsMetaData.Upsert(dat);
            Db.Checkpoint();
        }

        public void Bootstrap(string boostrapDirectory)
        {
            if (DbSymbolsMetaData.FindOne(e => true) == null)
                RebuildAllDatabase(boostrapDirectory, false);
            else
                throw new InvalidOperationException("Unable to bootstrap: db is not empty.");
        }


        public SymbolHistoryId[] ListAvailableData()
        {
            return SymbolsMetaData.Values.Select(md => md.HistoryId).ToArray();
        }

        public void SaveAndClose(SymbolHistoryId info, bool save = true)
        {
            var meta = GetMetaDataInternal(info);
            if (meta == null)
                throw new Exception("symbol history not found");

            if (save)
            {
                lock (DbSymbolsMetaData)
                {
                    meta.Flush(DataDir);
                    DbSymbolsMetaData.Upsert(meta);
                }
            }
            else
                meta.ClearView();

        }
        public void Delete(SymbolHistoryId id)
        {
            lock (SymbolsMetaData)
            {
                var meta = GetMetaDataInternal(id);
                if (meta == null)
                    throw new Exception("symbol history not found");


                foreach (var fileInfo in meta.Chunks.ToArray())
                {
                    if (File.Exists(fileInfo.GetFilePath(DataDir)))
                        File.Delete(fileInfo.GetFilePath(DataDir));
                    meta.Chunks.Remove(fileInfo);
                }
            }
        }
        public void Delete(string market, string symbol, TimeSpan time)
        {
            var histId = new SymbolHistoryId(market, symbol, time);
            Delete(histId);
        }

        public void ValidateData(SymbolHistoryId finfo)
        {
            lock (SymbolsMetaData)
            {
                var myData = GetMetaDataInternal(finfo);
                if (myData.View != null)
                {
                    var ticks = myData.View.TicksUnsafe;
                    lock (ticks)
                    {
                        for (int i = 1; i < ticks.Count; i++)
                        {
                            if (ticks[i].OpenTime < ticks[i - 1].OpenTime)
                            {
                                Console.WriteLine($"{finfo.Market} - {finfo.Symbol} - {finfo.Resolution} -> bad data at {i}");
                            }
                        }
                    }

                }
            }
        }

        public void FillGaps(SymbolHistoryId finfo)
        {
            lock (SymbolsMetaData)
            {
                var myData = GetMetaDataInternal(finfo);
                if (myData.View != null)
                {
                    //consolidator fills gaps by default
                    var consolidator = new TradeBarConsolidator(finfo.Resolution);
                    var ticks = myData.View.TicksUnsafe;


                    lock (ticks)
                    {
                        consolidator.OnConsolidated += it =>
                        {
                            if (it is Candlestick c)
                                ticks.Add(c);
                            else
                                ticks.Add(new Candlestick(it));
                        };

                        var oldTicks = new List<Candlestick>(ticks);
                        ticks.Clear();

                        foreach (var tick in oldTicks)
                            consolidator.Update(tick);
                    }

                }
            }
        }
        public void Dispose()
        {
            this.Db.Dispose();
        }
    }
}
