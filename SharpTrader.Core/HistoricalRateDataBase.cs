using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ProtoBuf;
using System.Globalization;
using System.Diagnostics;
namespace SharpTrader
{
    public class HistoryInfo
    {
        public string symbol;
        public string market;
        public TimeSpan timeframe;
        public HistoryInfo(string market, string symbol, TimeSpan frame)
        {
            this.market = market;
            this.symbol = symbol;
            this.timeframe = frame;
        }
        internal string GetFileMask()
        {
            return $"{this.market}_{this.symbol}_{(int)this.timeframe.TotalMilliseconds}";
        }
    }
    public class HistoryFileInfo : HistoryInfo
    {
        public DateTime Date;
        public string FilePath;
        public HistoryFileInfo(string filePath, string market, string symbol, TimeSpan frame, DateTime date) : base(market, symbol, frame)
        {
            FilePath = filePath;
            this.Date = date;
        }
        public HistoryFileInfo(string market, string symbol, TimeSpan frame, DateTime date) : base(market, symbol, frame)
        {
            this.Date = date;
        }
        public bool Equals(HistoryInfo info)
        {
            return this.market == info.market && this.symbol == info.symbol && this.timeframe == info.timeframe;
        }
        public string GetFileName()
        {
            return $"{market}_{symbol}_{(int)timeframe.TotalMilliseconds}_{Date.ToString("yyyyMM")}.bin";
        }
        public static HistoryFileInfo FromFileName(string fileName)
        {
            return null;
        }
    }
    //TODO make thread safe
    public class HistoricalRateDataBase
    {
        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        string BaseDirectory;
        private object SymbolsDataLocker = new object();
        List<SymbolHistoryRawExt> SymbolsData = new List<SymbolHistoryRawExt>();
        public HistoricalRateDataBase(string dataDir)
        {
            BaseDirectory = Path.Combine(dataDir, "RatesDB");
            if (!Directory.Exists(BaseDirectory))
                Directory.CreateDirectory(BaseDirectory);
            UpdateDtabaseVersion();
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
            var hist = GetHistoryRaw(histInfo, new DateTime(2019, 01, 01));
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
                            if (candleToAdd.OpenTime - lastAdded().OpenTime > histInfo.timeframe)
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
        private void UpdateDtabaseVersion()
        {
            //recreate the dbd
            Console.WriteLine("Updating history db");
            var allFilesInDir = Directory.GetFiles(BaseDirectory, "*.bin");
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
                                FileName = finfo.GetFileMask(),
                                Market = finfo.market,
                                Spread = 0,
                                Symbol = finfo.symbol,
                                Ticks = sdata.Ticks,
                                Timeframe = finfo.timeframe,
                                StartOfData = DateTime.MinValue
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
                        var shist2 = GetSymbolHistory(finfo, DateTime.MinValue);
                        Debug.Assert(shist2.Ticks.Count == shist.Ticks.Count, "Hist count doesn't match");
                        if (shist2.Ticks.Count != shist.Ticks.Count)
                        {
                            Console.WriteLine($"Hist count doesn't match for file conversion {oldFile}");
                            ok = false;
                        }
                        for (int i = 0; i < shist.Ticks.Count; i++)
                        {
                            shist2.Ticks.Next();
                            if (!shist.Ticks[i].Equals(shist2.Ticks.Tick))
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
        internal (ITradeBar first, ITradeBar last) GetFirstAndLastCandles(HistoryInfo historyInfo)
        {
            ITradeBar first = null;
            ITradeBar last = null;
            var infos = GetHistoryFiles(historyInfo).OrderBy(fi => fi.Date).ToArray();
            if (infos.Length > 0)
            {
                try
                {
                    using (var fs = File.Open(infos.First().FilePath, FileMode.Open))
                    {
                        SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                        first = fdata.Ticks.FirstOrDefault();
                    }
                    using (var fs = File.Open(infos.Last().FilePath, FileMode.Open))
                    {
                        SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                        last = fdata.Ticks.LastOrDefault();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception during GetFirstAndLastCandles: {ex.Message}");
                }
            }
            return (first, last);
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
                        Console.WriteLine($"{finfo.market} - {finfo.symbol} - {finfo.timeframe} -> bad data at {i}");
                    }
                }
            }
        }
        public void Delete(string market, string symbol, TimeSpan time)
        {
            var histInfo = new HistoryInfo(market, symbol, time);
            var filesInfos = GetHistoryFiles(histInfo);
            if (filesInfos.Length > 0)
            {
                lock (SymbolsDataLocker)
                {
                    for (int i = 0; i < SymbolsData.Count; i++)
                    {
                        if (SymbolsData[i].HistoryInfoEquals(histInfo))
                            SymbolsData.RemoveAt(i--);
                    }
                    foreach (var filePath in filesInfos.Select(fi => fi.FilePath))
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                }
            }
        }
        public HistoryInfo[] ListAvailableData()
        {
            var files = Directory.GetFiles(BaseDirectory, "*.bin");
            List<HistoryInfo> result = new List<HistoryInfo>();
            HashSet<string> resultsSet = new HashSet<string>();
            foreach (var filePath in files)
            {
                var elem = GetFileInfo(filePath);
                if (resultsSet.Add(elem.GetFileMask()))
                {
                    result.Add(elem);
                }
            }
            return result.ToArray();
        }
        public ISymbolHistory GetSymbolHistory(HistoryInfo info, DateTime startOfData)
        {
            var rawHist = GetHistoryRaw(info, startOfData);
            return new SymbolHistory(rawHist, startOfData);
        }
        private SymbolHistoryRaw GetHistoryRaw(HistoryInfo info, DateTime startOfData)
        {
            startOfData = new DateTime(startOfData.Year, startOfData.Month, 1);
            var endOfLoading = DateTime.MaxValue;
            lock (SymbolsDataLocker)
            {
                //check if we already have some records and load them  
                SymbolHistoryRawExt history = SymbolsData.FirstOrDefault(sd => sd.Equals(info));
                if (history == null)
                {
                    history = new SymbolHistoryRawExt
                    {
                        Ticks = new List<Candlestick>(),
                        FileName = info.GetFileMask(),
                        Market = info.market,
                        Spread = 0,
                        Symbol = info.symbol,
                        Timeframe = info.timeframe,
                        StartOfData = startOfData
                    };
                    SymbolsData.Add(history);
                }
                else
                {
                    endOfLoading = history.StartOfData;
                    history.StartOfData = startOfData;
                }
                //load missing months 
                var files = GetHistoryFiles(info);
                foreach (var finfo in files)
                {
                    if (finfo != null)
                    {
                        if (info.GetFileMask() == finfo.GetFileMask() && finfo.Date >= startOfData && finfo.Date < endOfLoading)
                        {
                            try
                            {
                                using (var fs = File.Open(finfo.FilePath, FileMode.Open))
                                {
                                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                                    Debug.Assert(history.FileName != null);
                                    AddCandlesToHistData(fdata.Ticks, history);
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
        public void AddCandlesticks(string market, string symbol, IEnumerable<ITradeBar> candles)
        {
            var hinfo = new HistoryInfo(market, symbol, candles.First().Timeframe);
            var sdata = GetHistoryRaw(hinfo, candles.First().OpenTime);
            //add data 
            //Debug.Assert(sdata.Ticks.Count > 0);
            //Debug.Assert(candles.First().OpenTime > sdata.Ticks.First().OpenTime, "Error in sdata times");
            AddCandlesToHistData(candles, sdata);
        }
        private static void AddCandlesToHistData(IEnumerable<ITradeBar> candles, SymbolHistoryRaw sdata)
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
                        var toAdd = new Candlestick(c);
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
        private HistoryFileInfo[] GetHistoryFiles(HistoryInfo info)
        {
            string fileNameMask = info.GetFileMask();
            var allFilesInDir = Directory.GetFiles(BaseDirectory, "*.bin");
            return allFilesInDir.Select(fp => GetFileInfo(fp)).Where(fi => fi != null && fi.GetFileMask() == info.GetFileMask()).ToArray();
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
            catch (Exception ex)
            {
                Debug.Assert(ex != null);
                //Console.WriteLine($"Error while parsing file info for file {fileName}");
            }
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
        public void CloseFile(string market, string symbol, TimeSpan frame)
        {
            CloseFile(new HistoryInfo(market, symbol, frame));
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
                    HistoryFileInfo finfo = new HistoryFileInfo(data.Market, data.Symbol, data.Timeframe, startDate);
                    finfo.FilePath = Path.Combine(BaseDirectory, finfo.GetFileName());
                    SymbolHistoryRaw sdata = new SymbolHistoryRaw()
                    {
                        FileName = finfo.GetFileName(),
                        Market = data.Market,
                        Spread = data.Spread,
                        Symbol = data.Symbol,
                        Ticks = candlesOfMont,
                        Timeframe = data.Timeframe
                    };
                    using (var fs = File.Open(finfo.FilePath, FileMode.Create))
                        Serializer.Serialize<SymbolHistoryRaw>(fs, sdata);
                }
            }
        }
        class SymbolHistoryRawExt : SymbolHistoryRaw
        {
            [ProtoIgnore]
            public DateTime StartOfData { get; set; }
            internal bool HistoryInfoEquals(HistoryInfo histInfo)
            {
                return this.Market == histInfo.market && this.Symbol == histInfo.symbol && this.Timeframe == histInfo.timeframe;
            }
        }
        [ProtoContract]
        class SymbolHistoryRaw
        {
            private List<Candlestick> _Ticks;
            public SymbolHistoryRaw()
            {
            }
            internal bool Equals(HistoryInfo info)
            {
                return info.market == Market && info.symbol == Symbol && info.timeframe == Timeframe;
            }
            [ProtoIgnore]
            public readonly object Locker = new object();
            [ProtoMember(6)]
            public virtual string FileName { get; set; }
            [ProtoMember(1)]
            public virtual string Market { get; set; }
            [ProtoMember(2)]
            public virtual string Symbol { get; set; }
            [ProtoMember(3)]
            public virtual TimeSpan Timeframe { get; set; }
            [ProtoMember(4)]
            public virtual List<Candlestick> Ticks
            {
                get { return _Ticks; }
                set
                {
                    if (_Ticks != null)
                        throw new Exception("Modification not allowed.");
                    else
                    {
                        _Ticks = value;
                    }
                }
            }
            [ProtoMember(5)]
            public virtual double Spread { get; set; }
        }
        class SymbolHistory : ISymbolHistory
        {
            public string Market { get; }
            public string Symbol { get; }
            public TimeSpan Timeframe { get; }
            public TimeSerieNavigator<ITradeBar> Ticks { get; }
            public double Spread { get; }
            public SymbolHistory(SymbolHistoryRaw raw, DateTime startOfData)
            {
                Market = raw.Market;
                Symbol = raw.Symbol;
                Timeframe = raw.Timeframe;
                Ticks = new TimeSerieNavigator<ITradeBar>(raw.Ticks.Where(t => t.Time >= startOfData));
                Spread = raw.Spread;
            }
            public SymbolHistory(SymbolHistoryRaw raw)
            {
                Market = raw.Market;
                Symbol = raw.Symbol;
                Timeframe = raw.Timeframe;
                Ticks = new TimeSerieNavigator<ITradeBar>(raw.Ticks);
                Spread = raw.Spread;
            }
        }
    }
    public interface ISymbolHistory
    {
        string Market { get; }
        string Symbol { get; }
        TimeSpan Timeframe { get; }
        TimeSerieNavigator<ITradeBar> Ticks { get; }
        double Spread { get; }
    }
}
