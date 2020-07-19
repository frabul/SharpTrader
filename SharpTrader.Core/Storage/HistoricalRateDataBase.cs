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

namespace SharpTrader.Storage
{
    //TODO make thread safe
    public class HistoricalRateDataBase
    {
        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        string DataDir;
        private object SymbolsDataLocker = new object();
        List<SymbolHistoryRawExt> SymbolsData = new List<SymbolHistoryRawExt>();
        public HistoricalRateDataBase(string dataDir)
        {
            DataDir = Path.Combine(dataDir, "RatesDB");
            if (!Directory.Exists(DataDir))
                Directory.CreateDirectory(DataDir);
            UpdateDtabaseVersion();
        }

        internal (ITradeBar first, ITradeBar last) GetFirstAndLastCandles(HistoryInfo historyInfo)
        {
            var metaData = SymbolsMetaData.FindById(historyInfo.GetKey());
            return (metaData.FirstBar, metaData.LastBar);
        }
        public void ValidateData(HistoryInfo finfo)
        {
            lock (SymbolsDataLocker)
            {
                SymbolHistoryRawExt myData;
                lock (SymbolsData)
                    myData = SymbolsData.First(sd => sd.HistoryInfoEquals(finfo));
                for (int i = 1; i < myData.Ticks.Count; i++)
                {
                    if (myData.Ticks[i].OpenTime < myData.Ticks[i - 1].OpenTime)
                    {
                        Console.WriteLine($"{finfo.market} - {finfo.symbol} - {finfo.Timeframe} -> bad data at {i}");
                    }
                }
            }
        }

        public void Delete(string market, string symbol, TimeSpan time)
        {
            var histInfo = new HistoryInfo(market, symbol, time);
            var filesInfos = GetHistoryFiles(histInfo);
            if (filesInfos.Count > 0)
            {
                lock (SymbolsDataLocker)
                {
                    for (int i = 0; i < SymbolsData.Count; i++)
                    {
                        if (SymbolsData[i].HistoryInfoEquals(histInfo))
                            SymbolsData.RemoveAt(i--);
                    }
                    foreach (var fileInfo in filesInfos.ToArray())
                    {
                        if (File.Exists(fileInfo.FilePath))
                            File.Delete(fileInfo.FilePath);
                        filesInfos.Remove(fileInfo);
                    }
                }
            }
        }

        public ISymbolHistory GetSymbolHistory(HistoryInfo info, DateTime startOfData, DateTime endOfData)
        {
            var rawHist = GetHistoryRaw(info, startOfData, endOfData);
            return new SymbolHistory(rawHist, startOfData);
        }

        public ISymbolHistory GetSymbolHistory(HistoryInfo info, DateTime startOfData)
        {
            return GetSymbolHistory(info, startOfData, DateTime.MaxValue);
        }

        public ISymbolHistory GetSymbolHistory(HistoryInfo info)
        {
            return GetSymbolHistory(info, DateTime.MinValue);
        }
         
        private SymbolHistoryRaw GetHistoryRaw(HistoryInfo historyInfo, DateTime startOfData, DateTime endOfData)
        {
            startOfData = new DateTime(startOfData.Year, startOfData.Month, 1);
            List<DateRange> dateRanges = new List<DateRange>();
            lock (SymbolsDataLocker)
            {
                //check if we already have some records and load them  
                SymbolHistoryRawExt history = SymbolsData.FirstOrDefault(sd => sd.Equals(historyInfo));
                if (history == null)
                {
                    history = new SymbolHistoryRawExt
                    {
                        Ticks = new List<Candlestick>(),
                        FileName = historyInfo.GetKey(),
                        Market = historyInfo.market,
                        Spread = 0,
                        Symbol = historyInfo.symbol,
                        Timeframe = historyInfo.Timeframe,
                        StartOfData = startOfData,
                        EndOfData = endOfData,

                    };
                    SymbolsData.Add(history);
                    dateRanges.Add(new DateRange(startOfData, endOfData));
                }
                else
                {
                    if (history.StartOfData > startOfData)
                    {
                        dateRanges.Add(new DateRange(startOfData, history.StartOfData));
                    }
                    if (endOfData > history.EndOfData)
                    {
                        dateRanges.Add(new DateRange(history.EndOfData, endOfData));
                    }
                }
                //load missing months 
                var files = GetHistoryFiles(historyInfo);
                foreach (var finfo in files)
                {
                    if (finfo != null)
                    {
                        var rightFile = historyInfo.GetKey() == finfo.GetKey();
                        var dateInRange = dateRanges.Any(dr => finfo.StartDate >= dr.start && finfo.StartDate < dr.end);
                        if (rightFile && dateInRange)
                        {
                            try
                            {
                                using (var fs = File.Open(finfo.FilePath, FileMode.Open))
                                {
                                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                                    Debug.Assert(history.FileName != null);
                                    AddCandlesToHistData(fdata.Ticks.Where(tick => tick.Time <= endOfData), history);
                                }
                            }
                            catch
                            {
                                Console.WriteLine($"ERROR ERROR loading file {finfo.FilePath}");
                            }
                        }
                    }
                }
                return history;
            }
        }

        public void AddCandlesticks(string market, string symbol, IEnumerable<Candlestick> candles)
        {
            var hinfo = new HistoryInfo(market, symbol, candles.First().Timeframe);
            var sdata = GetHistoryRaw(hinfo, candles.First().OpenTime, DateTime.MaxValue);
            //add data 
            //Debug.Assert(sdata.Ticks.Count > 0);
            //Debug.Assert(candles.First().OpenTime > sdata.Ticks.First().OpenTime, "Error in sdata times");
            AddCandlesToHistData(candles, sdata);
        }

        private static void AddCandlesToHistData(IEnumerable<Candlestick> candles, SymbolHistoryRaw sdata)
        {
            lock (sdata.Locker)
            {
                foreach (var c in candles)
                {
                    ITradeBar lastCandle = sdata.Ticks.Count > 0 ? sdata.Ticks[sdata.Ticks.Count - 1] : null;
                    if (c.Timeframe != sdata.Timeframe)
                    {
                        //throw new InvalidOperationException("Bad timeframe for candle");
                        Console.WriteLine("Bad timeframe for candle");
                    }
                    else
                    {
                        //if this candle open is preceding last candle open we need to insert it in sorted fashion
                        var toAdd = c; //new Candlestick(c);
                        if (lastCandle?.OpenTime > toAdd.OpenTime)
                        {
                            int i = sdata.Ticks.BinarySearch(toAdd, CandlestickTimeComparer);
                            int index = i;
                            if (i > -1)
                                sdata.Ticks[index] = toAdd;
                            else
                            {
                                index = ~i;
                                sdata.Ticks.Insert(index, toAdd);
                            }
                            if (index > 0)
                                Debug.Assert(sdata.Ticks[index].OpenTime >= sdata.Ticks[index - 1].OpenTime);
                            if (index + 1 < sdata.Ticks.Count)
                                Debug.Assert(sdata.Ticks[index].OpenTime <= sdata.Ticks[index + 1].OpenTime);

                        }
                        else
                            sdata.Ticks.Add(toAdd);
                    }
                }
            }
        }

        private List<HistoryFileInfo> GetHistoryFiles(HistoryInfo info)
        {
            var metaData = SymbolsMetaData.FindById(info.GetKey());
            if (metaData != null)
                return metaData.Chunks.ToList();
            else
                return new List<HistoryFileInfo>(0);
        }
         
        private HistoryInfo GetFileInfoV1(string fileName)
        {
            fileName = Path.GetFileName(fileName);
            string[] parts = fileName.Remove(fileName.Length - 4).Split('_');
            if (parts.Length == 3)
            {
                var market = parts[0];
                var symbol = parts[1];
                var time = TimeSpan.FromMilliseconds(int.Parse(parts[2]));
                return new HistoryInfo(market, symbol, time);
            }
            else
                return null;
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
        public void SaveAll()
        {
            lock (SymbolsDataLocker)
                foreach (var sdata in this.SymbolsData)
                    Save(sdata);
        }
        public void SaveAndClose(HistoryInfo info)
        {
            lock (SymbolsDataLocker)
            {
                var sdata = SymbolsData.FirstOrDefault(sd => sd.Equals(info));
                if (sdata == null)
                    throw new Exception("symbol history not found");
                Save(sdata);
                this.SymbolsData.Remove(sdata);
            }
        }
        public void CloseFile(HistoryInfo info)
        {
            lock (SymbolsDataLocker)
            {
                var sdata = SymbolsData.FirstOrDefault(sd => sd.Equals(info));
                lock (sdata.Locker)
                    this.SymbolsData.Remove(sdata);
            }
        }
        public void CloseAllFiles()
        {
            lock (SymbolsDataLocker)
            {
                foreach (var fi in SymbolsData.ToArray())
                {
                    lock (fi.Locker)
                    {
                        this.SymbolsData.Remove(fi);
                    }
                }
            }
        }
        private void Save(SymbolHistoryRaw sdata) => SaveProtobuf(sdata);
        private void SaveProtobuf(SymbolHistoryRaw data)
        {
            lock (data.Locker)
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

                    //update cache
                    var files = GetHistoryFiles(newInfo);
                    if (!files.Any(f => f.FilePath == newInfo.FilePath))
                        files.Add(newInfo);
                }
            }
        }
     

        //-----------------------------------------------------------
        private LiteDB.LiteDatabase Db;
        private LiteDB.ILiteCollection<SymbolHistoryMetaData> DbSymbolsMetaData;
        private Dictionary<string, SymbolHistoryMetaData> SymbolsMetaData;
        private void Init()
        {
            Db = new LiteDB.LiteDatabase(Path.Combine(this.DataDir, "MetaData.db"));
            DbSymbolsMetaData = Db.GetCollection<SymbolHistoryMetaData>("SymbolsMetaData");
            if (SymbolsMetaData.Count() == 0)
            {
                Update2();
            }
            SymbolsMetaData = DbSymbolsMetaData.FindAll().ToDictionary(md => md.Id);
        }


        private void Update2()
        {

            var data = new Dictionary<string, SymbolHistoryMetaData>();
            var files = Directory.GetFiles(DataDir, "*.bin");

            foreach (var filePath in files)
            {
                var fileInfo = GetFileInfo(filePath);
                //-- get metaData --
                if (!data.ContainsKey(fileInfo.GetKey()))
                    data.Add(fileInfo.GetKey(), new SymbolHistoryMetaData(fileInfo));
                var symData = data[fileInfo.GetKey()];

                symData.Chunks.Add(fileInfo);

                //update first and last tick time
                using (var fs = File.Open(fileInfo.FilePath, FileMode.Open))
                {
                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                    Debug.Assert(fdata.FileName == fileInfo.GetFileName());

                    if (fdata.Ticks.Count > 0)
                    {
                        if (symData.FirstBar.Time > fdata.Ticks.First().Time)
                            symData.FirstBar = fdata.Ticks.First();

                        if (symData.LastBar.Time < fdata.Ticks.Last().Time)
                            symData.LastBar = fdata.Ticks.Last();
                    }
                }
            }
            var allData = data.Values.ToArray();
            DbSymbolsMetaData.InsertBulk(allData);
            Db.Checkpoint();
        }

        public HistoryInfo[] ListAvailableData()
        {
            return SymbolsMetaData.Values.Select(md => md.Info).ToArray();
        }
        private void UpdateDtabaseVersion()
        {
            //recreate the dbd
            Console.WriteLine("Updating history db");
            var allFilesInDir = Directory.GetFiles(DataDir, "*.bin");
            foreach (var oldFile in allFilesInDir)
            {
                var finfo = GetFileInfoV1(oldFile);
                if (finfo != null)
                {
                    Console.WriteLine($"Updating history file {oldFile}");
                    bool ok = true;
                    SymbolHistoryRaw shist = null;
                    using (var fs = File.Open(oldFile, FileMode.Open))
                    {
                        try
                        {
                            var sdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                            var history = new SymbolHistoryRawExt()
                            {
                                FileName = finfo.GetKey(),
                                Market = finfo.market,
                                Spread = 0,
                                Symbol = finfo.symbol,
                                Ticks = sdata.Ticks,
                                Timeframe = finfo.Timeframe,
                                StartOfData = DateTime.MinValue,
                                EndOfData = DateTime.MaxValue,
                            };
                            SymbolsData.Add(history);
                            shist = history;
                        }
                        catch (Exception ex)
                        {
                            Debug.Assert(ex != null);
                            Console.WriteLine($"ERROR for file {oldFile}");
                        }
                    }
                    if (shist != null)
                    {
                        SaveAndClose(finfo);
                        var shist2 = GetSymbolHistory(finfo, DateTime.MinValue, DateTime.MaxValue);
                        Debug.Assert(shist2.Ticks.Count == shist.Ticks.Count, "Hist count doesn't match");
                        if (shist2.Ticks.Count != shist.Ticks.Count)
                        {
                            Console.WriteLine($"Hist count doesn't match for file conversion {oldFile}");
                            ok = false;
                        }
                        for (int i = 0; i < shist.Ticks.Count; i++)
                        {
                            shist2.Ticks.MoveNext();
                            if (!shist.Ticks[i].Equals(shist2.Ticks.Current))
                            {
                                Console.WriteLine($"ERROR validation during conversion for file {oldFile}");
                                Debug.Assert(false, "Hist count doesn't match");
                                ok = false;
                            }
                        }
                        CloseAllFiles();
                        if (ok)
                        {
                            File.Delete(oldFile);
                        }
                    }
                }
            }
            Console.WriteLine("Updating history db....completed");
        }

        public void FixDatabase(Func<string, DateTime, DateTime, Candlestick[]> downloadCandlesCallback)
        {
            foreach (var fi in ListAvailableData())
                FixSymbolHistory(fi, downloadCandlesCallback);
        }
        public void FixSymbolHistory(HistoryInfo histInfo, Func<string, DateTime, DateTime, Candlestick[]> downloadCandlesCallback)
        {
            int duplicates = 0;
            int matchErrors = 0;
            Console.WriteLine($"Checking {histInfo.symbol} history data ");
            var hist = GetHistoryRaw(histInfo, new DateTime(2019, 01, 01), DateTime.MaxValue);
            lock (SymbolsDataLocker)
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

    }
}
