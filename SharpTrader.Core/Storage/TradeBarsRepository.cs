using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ProtoBuf;
using System.Globalization;
using System.Diagnostics;
using LiteDB;
using BinanceExchange.API.Models.Response;
using System.Runtime.InteropServices;
using NLog;

namespace SharpTrader.Storage
{
    //TODO make thread safe
    public class TradeBarsRepository
    {
        private Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        private string DataDir;

        private LiteDatabase Db;
        private ILiteCollection<SymbolHistoryMetaDataInternal> DbSymbolsMetaData;
        private Dictionary<string, SymbolHistoryMetaDataInternal> SymbolsMetaData;

        public TradeBarsRepository(string dataDir)
        {
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
                }
                return metaData;
            }

        }
        public virtual ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData, DateTime endOfData)
        {
            var meta = GetMetaDataInternal(info);
            meta.LoadHistory(startOfData, endOfData);
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
            var meta = GetMetaDataInternal(symId);
            meta.LoadHistory(candles.First().OpenTime, DateTime.MaxValue);
            //add data 
            //Debug.Assert(sdata.Ticks.Count > 0);
            //Debug.Assert(candles.First().OpenTime > sdata.Ticks.First().OpenTime, "Error in sdata times");
            meta.AddBars(candles);
        }

     

        internal void UpdateFirstKnownData(SymbolHistoryId info, ITradeBar firstAvailable)
        {
            var data = GetMetaDataInternal(info);
            data.FirstKnownData = firstAvailable;
        }

        private void Init()
        {
            Db = new LiteDB.LiteDatabase(Path.Combine(this.DataDir, "Database.db"));
            Db.Pragma("UTC_DATE", true);

            DbSymbolsMetaData = Db.GetCollection<SymbolHistoryMetaDataInternal>("SymbolsMetaData");

            if (DbSymbolsMetaData.FindOne(e => true) == null)
            {
                Update1();
                Update2();
            }
            SymbolsMetaData = DbSymbolsMetaData.FindAll().ToDictionary(md => md.Id);
        }

        private void Update1()
        {
            //get a
            var files = Directory.GetFiles(DataDir, "*.bin");
            IEnumerable<IGrouping<string, string>> filesGrouped;
            if (files.Length > 0)
            {
                files = files.Concat(Directory.GetFiles(DataDir, "*.bin2")).ToArray();
                filesGrouped = files.GroupBy(f =>
                                               {
                                                   var info = HistoryChunkId.Parse(f);
                                                   return info.HistoryId.Key;
                                               }
                                );
                foreach (var group in filesGrouped)
                {
                    if (group.Count() > 1)
                    { }
                    var info = HistoryChunkId.Parse(group.First());
                    var histMetadata = new SymbolHistoryMetaDataInternal(info.HistoryId);
                    foreach (var fpath in group)
                    {
                        var fdata = HistoryChunk.Load(fpath);
                        histMetadata.AddBars(fdata.Ticks);
                        if (Path.GetExtension(fpath) == ".bin")
                            File.Delete(fpath);
                    }
                    histMetadata.Save(DataDir);
                }
            }


        }
        private void Update2()
        {
            var data = new Dictionary<string, SymbolHistoryMetaDataInternal>();
            var files = Directory.GetFiles(DataDir, "*.bin2");

            foreach (var filePath in files)
            {
                var fileInfo = HistoryChunkId.Parse(filePath);
                //-- get metaData --
                if (!data.ContainsKey(fileInfo.HistoryId.Key))
                    data.Add(fileInfo.HistoryId.Key, new SymbolHistoryMetaDataInternal(fileInfo.HistoryId));
                var symData = data[fileInfo.HistoryId.Key];

                symData.Chunks.Add(fileInfo);

                //update first and last tick time
                HistoryChunk fileData = HistoryChunk.Load(filePath);
                if (fileData.Ticks.Count > 0)
                {
                    symData.UpdateBars(fileData.Ticks.First());
                    symData.UpdateBars(fileData.Ticks.Last());
                }
            }
          
            var allData = data.Values.ToArray();
            DbSymbolsMetaData.InsertBulk(allData);
            Db.Checkpoint();
        }


        public SymbolHistoryId[] ListAvailableData()
        {
            return SymbolsMetaData.Values.Select(md => md.HistoryId).ToArray();
        }

        public void SaveAll()
        {
            lock (SymbolsMetaData)
                foreach (var sdata in this.SymbolsMetaData.Values)
                    Save(sdata);
        }

        public void SaveAndClose(SymbolHistoryId info, bool save = true)
        {
            var meta = GetMetaDataInternal(info);
            if (meta == null)
                throw new Exception("symbol history not found");

            if (save)
                Save(meta);
            meta.View = null;
        }

        private void Save(SymbolHistoryMetaDataInternal sdata)
        {
            lock (DbSymbolsMetaData)
            {
                sdata.Save(DataDir);
                DbSymbolsMetaData.Upsert(sdata); 
            }
        }

        public void Delete(string market, string symbol, TimeSpan time)
        {
            var histInfo = new SymbolHistoryId(market, symbol, time);
            lock (SymbolsMetaData)
            {
                var meta = GetMetaDataInternal(histInfo);
                if (meta == null)
                    throw new Exception("symbol history not found");


                foreach (var fileInfo in meta.Chunks.ToArray())
                {
                    if (File.Exists(fileInfo.FilePath))
                        File.Delete(fileInfo.FilePath);
                    meta.Chunks.Remove(fileInfo);
                }
            }
        }

        public void FixDatabase(Func<string, DateTime, DateTime, Candlestick[]> downloadCandlesCallback)
        {
            foreach (var fi in ListAvailableData())
                FixSymbolHistory(fi, DateTime.MinValue, DateTime.MaxValue, downloadCandlesCallback);
        }

        public void FixSymbolHistory(SymbolHistoryId histInfo, DateTime fromTime, DateTime toTime, Func<string, DateTime, DateTime, Candlestick[]> downloadCandlesCallback)
        {
            int duplicates = 0;
            int matchErrors = 0;
            Console.WriteLine($"Checking {histInfo.Symbol} history data ");
            var meta = GetMetaDataInternal(histInfo);
            meta.LoadHistory(fromTime, toTime);


            var oldTicks = meta.View.Ticks;
            var newTicks = new List<Candlestick>();
            Candlestick lastAdded() => newTicks[newTicks.Count - 1];
            if (oldTicks.Count > 0)
            {
                newTicks.Add(oldTicks[0]);
                for (int i = 1; i < oldTicks.Count; i++)
                {
                    var candleToAdd = oldTicks[i];
                    if (candleToAdd.OpenTime == lastAdded().OpenTime)
                    {
                        //we skip this
                        if (candleToAdd.Equals(lastAdded()))
                            matchErrors++;
                        duplicates++;
                    }
                    else if (candleToAdd.OpenTime < lastAdded().OpenTime)
                    {
                        Console.WriteLine($"   Bad candle at {candleToAdd.OpenTime}");
                    }
                    else
                    {
                        //check holes
                        if (candleToAdd.OpenTime - lastAdded().OpenTime > histInfo.Resolution)
                        {
                            //hole
                            Console.WriteLine($"   Hole found {lastAdded().OpenTime} -> {candleToAdd.OpenTime}");
                            //redownload data
                            if (downloadCandlesCallback != null)
                            {
                                var candlesToAdd = downloadCandlesCallback(histInfo.Symbol, lastAdded().OpenTime, candleToAdd.OpenTime)
                                                    .Where(c => c.OpenTime > lastAdded().OpenTime)
                                                    .OrderBy(c => c.OpenTime).ToArray();
                                newTicks.AddRange(candlesToAdd);
                                Console.WriteLine($"       Hole fixed!{candlesToAdd.FirstOrDefault()?.OpenTime} -> {candlesToAdd.LastOrDefault()?.OpenTime} ");
                            }
                        }
                        //finally add the candle
                        if (candleToAdd.OpenTime > lastAdded().OpenTime)
                            newTicks.Add(candleToAdd);
                    }
                }
                meta.View.Ticks.Clear();
                meta.View.Ticks.AddRange(newTicks);
                SaveAndClose(histInfo);
            }

            Console.WriteLine($"   Fixing {histInfo.Symbol} completed: duplicates {duplicates} - matchErrors {matchErrors}");
        }

        public void ValidateData(SymbolHistoryId finfo)
        {
            lock (SymbolsMetaData)
            {
                var myData = GetMetaDataInternal(finfo);
                if (myData.View != null)
                {
                    var ticks = myData.View.Ticks;
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
}
