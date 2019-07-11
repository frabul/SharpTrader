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
            //recreate the dbd
            Console.WriteLine("Updating history db");
            var allFilesInDir = Directory.GetFiles(BaseDirectory, "*.bin");
            foreach (var file in allFilesInDir)
            {
                var finfo = GetFileInfoV1(file);
                if (finfo != null)
                {
                    Console.WriteLine($"Updating history file {file}");
                    bool ok = true;
                    SymbolHistoryRaw shist = null;
                    using (var fs = File.Open(file, FileMode.Open))
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
                            Console.WriteLine($"ERROR for file {file}");
                        }
                    }


                    if (shist != null)
                    {
                        Save(finfo);
                        var shist2 = GetSymbolHistory(finfo);
                        Debug.Assert(shist2.Ticks.Count == shist.Ticks.Count, "Hist count doesn't match");
                        if (shist2.Ticks.Count != shist.Ticks.Count)
                        {
                            Console.WriteLine($"Hist count doesn't match for file conversion {file}");
                            ok = false;
                        }
                        for (int i = 0; i < shist.Ticks.Count; i++)
                        {
                            shist2.Ticks.Next();
                            if (!shist.Ticks[i].Equals(shist2.Ticks.Tick))
                            {
                                Console.WriteLine($"ERROR validation during conversion for file {file}");
                                Debug.Assert(false, "Hist count doesn't match");
                                ok = false;
                            }

                        }
                        CloseAllFiles();
                        if (ok)
                        {

                            File.Delete(file);
                        }
                    }
                }
            }
            Console.WriteLine("Updating history db....completed");
        }

        public void ValidateData(HistoryInfo finfo)
        {
            var data = this.GetHistoryRaw(finfo, DateTime.MinValue);
            for (int i = 1; i < data.Ticks.Count; i++)
            {
                if (data.Ticks[i].Time < data.Ticks[i - 1].Time)
                {
                    Console.WriteLine($"{finfo.market} - {finfo.symbol} - {finfo.timeframe} -> bad data at {i}");
                }
            }
        }

        public void Delete(string market, string symbol, TimeSpan time)
        {
            var finfo = new HistoryInfo(market, symbol, time);
            List<string> fileNames = GetHistoryFiles(finfo);

            if (fileNames.Count > 0)
            {
                var info = GetFileInfo(fileNames.First());
                lock (SymbolsDataLocker)
                {
                    for (int i = 0; i < SymbolsData.Count; i++)
                    {
                        if (SymbolsData[i].Market == info.market && SymbolsData[i].Symbol == info.symbol && SymbolsData[i].Timeframe == info.timeframe)
                            SymbolsData.RemoveAt(i--);
                    }

                    foreach (var fileName in fileNames)
                        if (File.Exists(Path.Combine(BaseDirectory, fileName)))
                            File.Delete(Path.Combine(BaseDirectory, fileName));
                }
            }
        }

        public HistoryFileInfo[] ListAvailableData()
        {
            var files = Directory.GetFiles(BaseDirectory, "*.bin");
            List<HistoryFileInfo> result = new List<HistoryFileInfo>();
            HashSet<string> resultsSet = new HashSet<string>();
            foreach (var file in files)
            {
                var elem = GetFileInfo(Path.GetFileName(file));
                if (resultsSet.Add(elem.market + elem.symbol + elem.timeframe.ToString()))
                {
                    result.Add(elem);
                }
            }
            return result.ToArray();
        }

        public ISymbolHistory GetSymbolHistory(string market, string symbol, TimeSpan frame)
        {
            return GetSymbolHistory(new HistoryInfo(market, symbol, frame), DateTime.MinValue);
        }

        public ISymbolHistory GetSymbolHistory(HistoryInfo info, DateTime startOfData)
        {
            var rawHist = GetHistoryRaw(info, startOfData);
            return new SymbolHistory(rawHist, startOfData);
        }

        public ISymbolHistory GetSymbolHistory(HistoryInfo info)
        {
            SymbolHistoryRaw sdata = GetHistoryRaw(info, DateTime.MinValue);
            return new SymbolHistory(sdata);
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
                foreach (var fileName in files)
                {
                    try
                    {
                        var finfo = GetFileInfo(fileName);
                        if (finfo != null)
                        {
                            if (info.GetFileMask() == finfo?.GetFileMask() && finfo.Date >= startOfData && finfo.Date < endOfLoading)
                            {
                                using (var fs = File.Open(fileName, FileMode.Open))
                                {
                                    SymbolHistoryRaw fdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                                    Debug.Assert(history.FileName != null);
                                    AddCandlesToHistData(fdata.Ticks, history); 
                                }
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"ERROR ERROR loading file {fileName}");
                    }
                }
                return history;
            } 
        }

        public void AddCandlesticks(string market, string symbol, IEnumerable<ICandlestick> candles)
        {
            //check if we already have some records and load them
            var timeframe = candles.First().Timeframe;
            var hinfo = new HistoryInfo(market, symbol, timeframe);
            var sdata = GetHistoryRaw(hinfo, DateTime.MinValue);
            //add data 
            AddCandlesToHistData(candles, sdata);
        }

        private static void AddCandlesToHistData(IEnumerable<ICandlestick> candles, SymbolHistoryRaw sdata)
        {
            lock (sdata.Locker)
            {
                foreach (var c in candles)
                {
                    ICandlestick lastCandle = sdata.Ticks.Count > 0 ? sdata.Ticks[sdata.Ticks.Count - 1] : null;
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
                            int index = sdata.Ticks.BinarySearch(toAdd, CandlestickTimeComparer);
                            if (index > -1)
                                sdata.Ticks[index] = toAdd;
                            else
                                sdata.Ticks.Insert(~index, toAdd);
                        }
                        else
                            sdata.Ticks.Add(toAdd);
                    }
                }
            }
        }

        private List<string> GetHistoryFiles(HistoryInfo info)
        {
            string fileNameMask = info.GetFileMask();
            var allFilesInDir = Directory.GetFiles(BaseDirectory, "*.bin");
            List<string> fileNames = new List<string>();
            foreach (var file in allFilesInDir)
            {
                file.StartsWith(fileNameMask);
                fileNames.Add(file);
            }
            return fileNames;
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

        private HistoryFileInfo GetFileInfo(string fileName)
        {
            HistoryFileInfo ret = null;
            try
            {
                fileName = Path.GetFileName(fileName);
                string[] parts = fileName.Remove(fileName.Length - 4).Split('_');
                if (parts.Length > 3)
                {
                    var market = parts[0];
                    var symbol = parts[1];
                    var time = TimeSpan.FromMilliseconds(int.Parse(parts[2]));
                    var date = DateTime.ParseExact(parts[3], "yyyyMM", CultureInfo.InvariantCulture);
                    ret = new HistoryFileInfo(market, symbol, time, date);
                }
            }
            catch (Exception ex)
            {
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

        public void Save(HistoryInfo info)
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
                var fileName = GetHistoryFiles(info);
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
                    DateTime startDate = new DateTime(data.Ticks[i].Time.Year, data.Ticks[i].Time.Month, 1);
                    DateTime endDate = startDate.AddMonths(1);
                    List<Candlestick> candlesOfMont = new List<Candlestick>();
                    while (i < data.Ticks.Count && data.Ticks[i].Time < endDate)
                    {
                        candlesOfMont.Add(new Candlestick(data.Ticks[i]));
                        i++;
                    }

                    HistoryFileInfo finfo = new HistoryFileInfo(data.Market, data.Symbol, data.Timeframe, startDate);
                    SymbolHistoryRaw sdata = new SymbolHistoryRaw()
                    {
                        FileName = finfo.GetFileName(),
                        Market = data.Market,
                        Spread = data.Spread,
                        Symbol = data.Symbol,
                        Ticks = candlesOfMont,
                        Timeframe = data.Timeframe
                    };

                    using (var fs = File.Open(Path.Combine(BaseDirectory, sdata.FileName), FileMode.Create))
                        Serializer.Serialize<SymbolHistoryRaw>(fs, sdata);
                }
            }
        }

        class SymbolHistoryRawExt : SymbolHistoryRaw
        {




            [ProtoIgnore]
            public DateTime StartOfData { get; set; }
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

            public TimeSerieNavigator<ICandlestick> Ticks { get; }

            public double Spread { get; }

            public SymbolHistory(SymbolHistoryRaw raw, DateTime startOfData)
            {
                Market = raw.Market;
                Symbol = raw.Symbol;
                Timeframe = raw.Timeframe;

                Ticks = new TimeSerieNavigator<ICandlestick>(raw.Ticks.Where(t => t.Time >= startOfData));
                Spread = raw.Spread;
            }

            public SymbolHistory(SymbolHistoryRaw raw)
            {
                Market = raw.Market;
                Symbol = raw.Symbol;
                Timeframe = raw.Timeframe;
                Ticks = new TimeSerieNavigator<ICandlestick>(raw.Ticks);
                Spread = raw.Spread;
            }
        }
    }

    public interface ISymbolHistory
    {
        string Market { get; }
        string Symbol { get; }
        TimeSpan Timeframe { get; }
        TimeSerieNavigator<ICandlestick> Ticks { get; }
        double Spread { get; }
    }




}
