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

namespace SharpTrader.Storage
{
    //TODO make thread safe
    public class TradeBarsRepository
    {
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
                if (!SymbolsMetaData.TryGetValue(historyInfo.GetKey(), out var metaData))
                {
                    metaData = new SymbolHistoryMetaDataInternal(historyInfo);
                    SymbolsMetaData.Add(historyInfo.GetKey(), metaData);
                }
                return metaData;
            }

        }
        public ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData, DateTime endOfData)
        {
            var rawHist = GetHistoryRaw(info, startOfData, endOfData);
            return new SymbolHistory(rawHist, startOfData, endOfData);
        }

        public ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData)
        {
            return GetSymbolHistory(info, startOfData, DateTime.MaxValue);
        }

        public ISymbolHistory GetSymbolHistory(SymbolHistoryId info)
        {
            return GetSymbolHistory(info, DateTime.MinValue);
        }

        private SymbolHistoryRaw GetHistoryRaw(SymbolHistoryId historyInfo, DateTime startOfData, DateTime endOfData)
        {
            var meta = GetMetaDataInternal(historyInfo);
            startOfData = new DateTime(startOfData.Year, startOfData.Month, 1);
            List<DateRange> missingData = new List<DateRange>();
            lock (meta.Locker)
            {
                //check if we already have some records and load them   
                if (meta.Ticks.Ticks.Count == 0)
                {
                    missingData.Add(new DateRange(startOfData, endOfData));
                }
                else
                {
                    if (meta.Ticks.StartOfData > startOfData)
                    {
                        missingData.Add(new DateRange(startOfData, meta.Ticks.StartOfData));
                    }
                    if (endOfData > meta.Ticks.EndOfData)
                    {
                        missingData.Add(new DateRange(meta.Ticks.EndOfData, endOfData));
                    }
                }

                //load missing data  
                foreach (var finfo in meta.Chunks)
                {
                    if (finfo != null)
                    {
                        var rightFile = historyInfo.GetKey() == finfo.GetKey();
                        var dateInRange = missingData.Any(dr => finfo.StartDate >= dr.start && finfo.StartDate < dr.end);
                        if (rightFile && dateInRange)
                        {
                            try
                            {
                                using (var fs = File.Open(finfo.FilePath, FileMode.Open))
                                {
                                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                                    meta.AddBars(fdata.Ticks.Where(tick => tick.Time <= endOfData));
                                }
                            }
                            catch
                            {
                                Console.WriteLine($"ERROR loading file {finfo.FilePath}");
                            }
                        }
                    }
                }

                return meta.Ticks;
            }
        }

        public void AddCandlesticks(string market, string symbol, IEnumerable<Candlestick> candles)
        {
            var hinfo = new SymbolHistoryId(market, symbol, candles.First().Timeframe);
            var sdata = GetHistoryRaw(hinfo, candles.First().OpenTime, DateTime.MaxValue);
            var meta = GetMetaDataInternal(hinfo);
            //add data 
            //Debug.Assert(sdata.Ticks.Count > 0);
            //Debug.Assert(candles.First().OpenTime > sdata.Ticks.First().OpenTime, "Error in sdata times");
            meta.AddBars(candles);
        }
         
        private HistoryFileInfo GetFileInfo(string filePath)
        {
            HistoryFileInfo ret = null;
            try
            {
                var fileName = Path.GetFileName(filePath);
                string[] parts = fileName.Remove(fileName.Length - 4).Split('_');
                if (parts.Length > 3)
                {
                    var market = parts[0];
                    var symbol = parts[1];
                    var time = TimeSpan.FromMilliseconds(int.Parse(parts[2]));
                    var date = DateTime.ParseExact(parts[3], "yyyyMM", CultureInfo.InvariantCulture);
                    ret = new HistoryFileInfo(filePath, market, symbol, time, date);
                }
            }
            catch (Exception _ex)
            {
                Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }
            Debug.Assert(ret != null);
            return ret;
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
            if (SymbolsMetaData.Count() == 0)
            {
                Update2();
            }
            SymbolsMetaData = DbSymbolsMetaData.FindAll().ToDictionary(md => md.Id);
        }

        private void Update2()
        {
            var data = new Dictionary<string, SymbolHistoryMetaDataInternal>();
            var files = Directory.GetFiles(DataDir, "*.bin");

            foreach (var filePath in files)
            {
                var fileInfo = GetFileInfo(filePath);
                //-- get metaData --
                if (!data.ContainsKey(fileInfo.GetKey()))
                    data.Add(fileInfo.GetKey(), new SymbolHistoryMetaDataInternal(fileInfo));
                var symData = data[fileInfo.GetKey()];

                symData.Chunks.Add(fileInfo);

                //update first and last tick time
                using (var fs = File.Open(fileInfo.FilePath, FileMode.Open))
                {
                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                    Debug.Assert(fdata.FileName == fileInfo.GetFileName());

                    if (fdata.Ticks.Count > 0)
                    {
                        symData.UpdateBars(fdata.Ticks.First());
                        symData.UpdateBars(fdata.Ticks.Last());
                    }
                }
            }
            foreach (var symData in data.Values)
            {
                symData.Chunks = symData.Chunks.OrderBy(f => f.StartDate).ToList();
            }
            var allData = data.Values.ToArray();
            DbSymbolsMetaData.InsertBulk(allData);
            Db.Checkpoint();
        }

        public SymbolHistoryId[] ListAvailableData()
        {
            return SymbolsMetaData.Values.Select(md => md.Info).ToArray();
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
            lock (meta.Locker)
            {
                if (save)
                    Save(meta);
                meta.Ticks = null;
            }
        }

        private void Save(SymbolHistoryMetaDataInternal sdata)
        {
            lock (sdata.Locker)
            {
                DbSymbolsMetaData.Upsert(sdata);
                SaveTicks_Protobuf(sdata.Ticks);
            }
        }

        private void SaveTicks_Protobuf(SymbolHistoryRaw data)
        {

            int i = 0;
            while (i < data.Ticks.Count)
            {
                DateTime startDate = new DateTime(data.Ticks[i].OpenTime.Year, data.Ticks[i].OpenTime.Month, 1);
                DateTime endDate = startDate.AddMonths(1);
                List<Candlestick> candlesOfMont = new List<Candlestick>();
                while (i < data.Ticks.Count && data.Ticks[i].OpenTime < endDate)
                {
                    candlesOfMont.Add(new Candlestick(data.Ticks[i]));
                    i++;
                }
                HistoryFileInfo newInfo = new HistoryFileInfo(data.Market, data.Symbol, data.Timeframe, startDate);
                newInfo.FilePath = Path.Combine(DataDir, newInfo.GetFileName());
                SymbolHistoryRaw sdata = new SymbolHistoryRaw()
                {
                    FileName = newInfo.GetFileName(),
                    Market = data.Market,
                    Spread = data.Spread,
                    Symbol = data.Symbol,
                    Ticks = candlesOfMont,
                    Timeframe = data.Timeframe
                };
                using (var fs = File.Open(newInfo.FilePath, FileMode.Create))
                    Serializer.Serialize<SymbolHistoryRaw>(fs, sdata);
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
                FixSymbolHistory(fi, downloadCandlesCallback);
        }
        public void FixSymbolHistory(SymbolHistoryId histInfo, Func<string, DateTime, DateTime, Candlestick[]> downloadCandlesCallback)
        {
            int duplicates = 0;
            int matchErrors = 0;
            Console.WriteLine($"Checking {histInfo.symbol} history data ");
            var hist = GetHistoryRaw(histInfo, new DateTime(2019, 01, 01), DateTime.MaxValue);
            var meta = GetMetaDataInternal(histInfo);
            lock (meta.Locker)
            {
                var oldTicks = hist.Ticks;
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
                            if (candleToAdd.OpenTime - lastAdded().OpenTime > histInfo.Timeframe)
                            {
                                //hole
                                Console.WriteLine($"   Hole found {lastAdded().OpenTime} -> {candleToAdd.OpenTime}");
                                //redownload data
                                if (downloadCandlesCallback != null)
                                {
                                    var candlesToAdd = downloadCandlesCallback(histInfo.symbol, lastAdded().OpenTime, candleToAdd.OpenTime)
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
                    hist.Ticks.Clear();
                    hist.Ticks.AddRange(newTicks);
                    SaveAndClose(histInfo);
                }
            }

            Console.WriteLine($"   Fixing {histInfo.symbol} completed: duplicates {duplicates} - matchErrors {matchErrors}");
        }

        public void ValidateData(SymbolHistoryId finfo)
        {
            lock (SymbolsMetaData)
            {
                var myData = GetMetaDataInternal(finfo);
                if (myData.Ticks != null)
                {
                    var ticks = myData.Ticks.Ticks;
                    for (int i = 1; i < ticks.Count; i++)
                    {
                        if (ticks[i].OpenTime < ticks[i - 1].OpenTime)
                        {
                            Console.WriteLine($"{finfo.market} - {finfo.symbol} - {finfo.Timeframe} -> bad data at {i}");
                        }
                    }
                }
            }
        }

    }
}
