using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ProtoBuf;

namespace SharpTrader
{
    //TODO make thread safe
    public class HistoricalRateDataBase
    {

        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        string BaseDirectory;
        private object SymbolsDataLocker = new object();
        List<SymbolHistoryRaw> SymbolsData = new List<SymbolHistoryRaw>();

        public HistoricalRateDataBase(string dataDir)
        {
            BaseDirectory = dataDir + "RatesDB\\";
            if (!Directory.Exists(BaseDirectory))
                Directory.CreateDirectory(BaseDirectory);
        }

        public void ValidateData(string market, string symbol, TimeSpan time)
        {
            var data = this.GetHistoryRaw(market, symbol, time);
            for (int i = 1; i < data.Ticks.Count; i++)
            {
                if (data.Ticks[i].Time < data.Ticks[i - 1].Time)
                {
                    Console.WriteLine($"{market} - {symbol} - {time} -> bad data at {i}");
                }
            }
        }

        public void Delete(string market, string symbol, TimeSpan time)
        {
            string fileName = GetFileName(market, symbol, time);
            lock (SymbolsDataLocker)
            {
                for (int i = 0; i < SymbolsData.Count; i++)
                {
                    if (SymbolsData[i].FileName == fileName)
                        SymbolsData.RemoveAt(i--);
                }

                if (File.Exists(BaseDirectory + fileName))
                    File.Delete(BaseDirectory + fileName);
            }
        }

        public (string market, string symbol, TimeSpan time)[] ListAvailableData()
        {
            var files = Directory.GetFiles(BaseDirectory, "*.bin");
            List<(string market, string symbol, TimeSpan time)> result = new List<(string market, string symbol, TimeSpan time)>();
            foreach (var file in files)
            {
                var elem = GetFileInfo(Path.GetFileName(file));
                result.Add(elem);
            }
            return result.ToArray();
        }

        public void LoadAll()
        {
            var files = Directory.GetFiles(BaseDirectory, "*.bin");
            foreach (var file in files)
            {
                using (var fs = File.Open(file, FileMode.Open))
                {
                    var sdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                    SymbolsData.Add(sdata);
                }
            }
        }

        public ISymbolHistory GetSymbolHistory(string market, string symbol, TimeSpan timeframe)
        {
            SymbolHistoryRaw sdata = GetHistoryRaw(market, symbol, timeframe);
            return new SymbolHistory(sdata);
        }

        private SymbolHistoryRaw GetHistoryRaw(string market, string symbol, TimeSpan timeframe)
        {
            //check if we already have some records and load them 
            var fileName = GetFileName(market, symbol, timeframe);
            SymbolHistoryRaw sdata = null;
            lock (SymbolsDataLocker)
            {
                sdata = SymbolsData.FirstOrDefault(sd => sd.FileName == fileName);
                if (sdata == null)
                {
                    //check if we have it on disk
                    if (File.Exists(BaseDirectory + fileName))
                    {
                        using (var fs = File.Open(BaseDirectory + fileName, FileMode.Open))
                            sdata = Serializer.Deserialize<SymbolHistoryRaw>(fs);
                    }
                    else
                    {
                        sdata = new SymbolHistoryRaw()
                        {
                            FileName = fileName,
                            Market = market,
                            Spread = 0,
                            Symbol = symbol,
                            Ticks = new List<Candlestick>(),
                            Timeframe = timeframe,
                        };
                    }

                    SymbolsData.Add(sdata);
                }
                else
                {
                    if (sdata.Ticks.Count > 0)
                    {
                        var tf = sdata.Ticks.FirstOrDefault()?.Timeframe;
                        if (tf != timeframe)
                            throw new InvalidOperationException("Bad timeframe for candle");
                    }

                }
            }
            return sdata;
        }

        public void AddCandlesticks(string market, string symbol, IEnumerable<ICandlestick> candles)
        {
            //check if we already have some records and load them
            var timeframe = candles.First().Timeframe;
            var sdata = GetHistoryRaw(market, symbol, timeframe);
            //add data 
            lock (sdata.Locker)
            {

                foreach (var c in candles)
                {
                    ICandlestick lastCandle = sdata.Ticks.Count > 0 ? sdata.Ticks[sdata.Ticks.Count - 1] : null;
                    if (c.Timeframe != timeframe)
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

        private string GetFileName(string market, string symbol, TimeSpan timeframe)
        {

            string fileName = $"{market}_{symbol}_{(int)timeframe.TotalMilliseconds}.bin";
            return fileName;
        }

        private (string market, string symbol, TimeSpan timeframe) GetFileInfo(string fileName)
        {
            string[] parts = fileName.Remove(fileName.Length - 4).Split('_');
            var market = parts[0];
            var symbol = parts[1];
            var time = TimeSpan.FromMilliseconds(int.Parse(parts[2]));
            return (market, symbol, time);
        }

        public void SaveAll()
        {
            lock (SymbolsDataLocker)
                foreach (var sdata in this.SymbolsData)
                    Save(sdata);
        }

        public void Save(string market, string symbol, TimeSpan timeframe)
        {
            lock (SymbolsDataLocker)
            {
                var fileName = GetFileName(market, symbol, timeframe);
                var sdata = SymbolsData.FirstOrDefault(sd => sd.FileName == fileName);

                if (sdata == null)
                    throw new Exception("symbol history not found");

                Save(sdata);

                this.SymbolsData.Remove(sdata);
            }
        }

        private void Save(SymbolHistoryRaw sdata) => SaveProtobuf(sdata);

        private void SaveProtobuf(SymbolHistoryRaw data)
        {

            lock (data.Locker)
            {
                SymbolHistoryRaw sdata = new SymbolHistoryRaw()
                {
                    FileName = data.FileName,
                    Market = data.Market,
                    Spread = data.Spread,
                    Symbol = data.Symbol,
                    Ticks = data.Ticks.Select(c => new Candlestick(c)).ToList(),
                    Timeframe = data.Timeframe
                };
                using (var fs = File.Open(BaseDirectory + data.FileName, FileMode.Create))
                    Serializer.Serialize<SymbolHistoryRaw>(fs, sdata);
            }
        }


        [ProtoContract]
        class SymbolHistoryRaw
        {
            private List<Candlestick> _Ticks;

            public SymbolHistoryRaw()
            {

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
