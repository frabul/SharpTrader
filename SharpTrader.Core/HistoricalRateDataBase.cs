using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZeroFormatter;
using System.IO;
using ProtoBuf;

namespace SharpTrader
{
    //TODO make thread safe
    public class HistoricalRateDataBase
    {

        private static readonly CandlestickTimeComparer<Candlestick> CandlestickTimeComparer = new CandlestickTimeComparer<Candlestick>();
        string BaseDirectory;
        List<SymbolHistoryRaw> SymbolsData = new List<SymbolHistoryRaw>();

        public HistoricalRateDataBase(string dataDir)
        {
            BaseDirectory = dataDir + "\\RatesDB\\";
            if (!Directory.Exists(BaseDirectory))
                Directory.CreateDirectory(BaseDirectory);
        }

        public (string market, string symbol, TimeSpan time)[] ListAvailableData()
        {
            var files = Directory.GetFiles(BaseDirectory, "*.bin");
            List<(string market, string symbol, TimeSpan time)> result = new List<(string market, string symbol, TimeSpan time)>();
            foreach (var file in files)
            {
                var elem = GetFileInfo(file);
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
            //check if we already have some records and load them 
            var fileName = GetFileName(market, symbol, timeframe);
            var sdata = SymbolsData.FirstOrDefault(sd => sd.FileName == fileName);
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
                var tf = sdata.Ticks.FirstOrDefault()?.Timeframe;
                if (tf != timeframe)
                    throw new InvalidOperationException("Bad timeframe for candle");
            }
            return sdata;
        }

        public void AddCandlesticks(string market, string symbol, IEnumerable<ICandlestick> candles)
        {
            //check if we already have some records and load them
            var timeframe = candles.First().Timeframe;
            var sdata = (SymbolHistoryRaw)GetSymbolHistory(market, symbol, timeframe);
            //add data 
            lock (sdata.Locker)
            {
                ICandlestick lastCandle = sdata.Ticks.LastOrDefault();
                foreach (var c in candles)
                {
                    if (c.Timeframe != timeframe)
                        throw new InvalidOperationException("Bad timeframe for candle");
                    //if this candle open is preceding last candle open we need to insert it in sorted fashion
                    var toAdd = new Candlestick(c);
                    if (lastCandle?.Close > c.Open)
                    {
                        int index = sdata.Ticks.BinarySearch(toAdd, CandlestickTimeComparer);
                        if (index > -1)
                            sdata.Ticks[index] = toAdd;
                        else
                            sdata.Ticks.Insert(~index, toAdd);
                    }
                    else
                        sdata.Ticks.Add(new Candlestick(c));
                    lastCandle = c;
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
            foreach (var sdata in this.SymbolsData)
                Save(sdata);
        }

        public void Save(string market, string symbol, TimeSpan timeframe)
        {
            var fileName = GetFileName(market, symbol, timeframe);
            var sdata = SymbolsData.FirstOrDefault(sd => sd.FileName == fileName);

            if (sdata == null)
                throw new Exception("symbol history not found");

            Save(sdata);

            this.SymbolsData.Remove(sdata);
        }

        private void Save(SymbolHistoryRaw sdata) => SaveProtobuf(sdata);

        private void SaveZeroFormatter(SymbolHistoryRaw data)
        {
            lock (data.Locker)
            {
                using (var fs = File.Open(BaseDirectory + data.FileName, FileMode.Create))
                    ZeroFormatterSerializer.Serialize(fs, data);
            }
        }

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


        [ZeroFormattable, ProtoContract]
        class SymbolHistoryRaw : ISymbolHistory
        {
            private List<Candlestick> _Ticks;
            private CandlesticksSerieNavigator TicksNavigator;

            public SymbolHistoryRaw()
            {

            }

            [IgnoreFormat, ProtoIgnore]
            public readonly object Locker = new object();

            [Index(5), ProtoMember(6)]
            public virtual string FileName { get; set; }

            [Index(0), ProtoMember(1)]
            public virtual string Market { get; set; }

            [Index(1), ProtoMember(2)]
            public virtual string Symbol { get; set; }

            [Index(2), ProtoMember(3)]
            public virtual TimeSpan Timeframe { get; set; }

            [Index(3), ProtoMember(4)]
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
                        TicksNavigator = new CandlesticksSerieNavigator(value);
                    }
                }
            }

            [Index(4), ProtoMember(5)]
            public virtual double Spread { get; set; }

            [IgnoreFormat, ProtoIgnore]
            CandlesticksSerieNavigator ISymbolHistory.Ticks => TicksNavigator;
        }
    }

    public interface ISymbolHistory
    {
        string Market { get; }
        string Symbol { get; }
        TimeSpan Timeframe { get; }
        CandlesticksSerieNavigator Ticks { get; }
        double Spread { get; }
    }




}
