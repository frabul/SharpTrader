using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;

namespace SharpTrader.Storage
{
    class HistoryView
    {
        Logger Logger = LogManager.GetLogger("HistoryView");
        private SymbolHistoryMetaDataInternal SymbolHistory;
        private List<Candlestick> _Ticks = new List<Candlestick>();
        public SymbolHistoryId Id { get; set; }
        public HashSet<HistoryChunkId> LoadedFiles { get; set; } = new HashSet<HistoryChunkId>();
        public DateTime StartOfData { get; set; } = DateTime.MaxValue;
        public DateTime EndOfData { get; set; } = DateTime.MinValue;

        public HistoryView(SymbolHistoryId id)
        {
            Id = id;
            _Ticks = new List<Candlestick>();
        }

        internal TimeSerieNavigator<ITradeBar> GetNavigatorFromTicks(Func<ITradeBar, bool> f)
        {
            lock (_Ticks)
                return new TimeSerieNavigator<ITradeBar>(_Ticks.Where(f));
        }

        internal TimeSerieNavigator<ITradeBar> GetNavigatorFromTicks()
        {
            lock (_Ticks)
                return new TimeSerieNavigator<ITradeBar>(_Ticks);
        }

        public virtual List<Candlestick> TicksUnsafe
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

        private List<Candlestick> Ticks
        {
            get { return (_Ticks); }
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

        public int TicksCount => _Ticks.Count;

        private void UpdateStartAndEnd(ITradeBar bar)
        {
            if (this.EndOfData < bar.Time)
                this.EndOfData = bar.Time;

            if (this.StartOfData > bar.Time)
                this.StartOfData = bar.Time;
        }

        /// <summary>
        /// Adds a tradebar to currently loaded set
        /// </summary>
        /// <param name="c"></param>
        public void AddBar(Candlestick c)
        {
            lock (_Ticks)
            {
                ITradeBar lastCandle = this.Ticks.Count > 0 ? this.Ticks[this.Ticks.Count - 1] : null;
                if (c.Timeframe != this.Id.Resolution)
                {
                    //throw new InvalidOperationException("Bad timeframe for candle");
                    Logger.Error("Bad timeframe for candle");
                }
                else
                {
                    this.UpdateStartAndEnd(c);
                    //if this candle open is preceding last candle open we need to insert it in sorted fashion
                    var toAdd = c; //new Candlestick(c);
                    if (lastCandle != null && lastCandle.OpenTime >= toAdd.OpenTime)
                    {
                        int i = this.Ticks.BinarySearch(toAdd, CandlestickTimeComparer.Instance);
                        int index = i;
                        if (i > -1)
                            this.Ticks[index] = toAdd;
                        else
                        {
                            index = ~i;
                            this.Ticks.Insert(index, toAdd);
                        }
                        if (index > 0)
                            Debug.Assert(this.Ticks[index].OpenTime > this.Ticks[index - 1].OpenTime);
                        if (index + 1 < this.Ticks.Count)
                            Debug.Assert(this.Ticks[index].OpenTime < this.Ticks[index + 1].OpenTime);
                    }
                    else
                        this.Ticks.Add(toAdd);
                }
            }

        }

        internal void Save(string dataDir, ChunkFileVersion chunkFileVersion, ChunkSpan chunkSpan)
        {
            Func<HistoryChunkId, List<Candlestick>, HistoryChunk> chunkFactory;
            Func<SymbolHistoryId, DateTime, DateTime, HistoryChunkId> idFactory;


            switch (chunkFileVersion)
            {
                case ChunkFileVersion.V2:
                    chunkFactory = (newChunkId, candles) =>
                    {
                        return new HistoryChunkV2()
                        {
                            ChunkId = newChunkId,
                            Ticks = candles,
                        };
                    };
                    idFactory = (symId, t1, t2) => new HistoryChunkIdV2(symId, t1);
                    if (chunkSpan != ChunkSpan.OneMonth)
                        throw new InvalidOperationException("The chunk file version V2 supports only OneMonth span");
                    break;
                case ChunkFileVersion.V3:
                    chunkFactory = (newChunkId, candles) =>
                    {
                        return new HistoryChunkV3()
                        {
                            ChunkId = newChunkId,
                            Ticks = candles,
                        };
                    };
                    idFactory = (symId, t1, t2) => new HistoryChunkIdV3(symId, t1, t2);
                    break;
                default:
                    throw new NotImplementedException($"Unknown ChunkFileVersion {chunkFileVersion}");
            }

            switch (chunkSpan)
            {
                case ChunkSpan.OneMonth:
                    Save_month(dataDir, idFactory, chunkFactory);
                    break;
                case ChunkSpan.OneDay:
                    Save_daily(dataDir, idFactory, chunkFactory);
                    break;
            }




        }

        /// <summary>
        /// Saves all data to disk and updates LoadedFiles list
        /// </summary>
        /// <param name="dataDir"></param>
        public void Save_month(
            string dataDir,
            Func<SymbolHistoryId, DateTime, DateTime, HistoryChunkId> idFactory,
            Func<HistoryChunkId, List<Candlestick>, HistoryChunk> chunkFactory)
        {
            lock (_Ticks)
            {
                int i = 0;
                while (i < this.Ticks.Count)
                {
                    DateTime startDate = new DateTime(this.Ticks[i].OpenTime.Year, this.Ticks[i].OpenTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    DateTime endDate = startDate.AddMonths(1);
                    TimeSerie<Candlestick> candlesOfMont = new TimeSerie<Candlestick>();
                    while (i < this.Ticks.Count && this.Ticks[i].OpenTime < endDate)
                    {
                        candlesOfMont.AddRecord(new Candlestick(this.Ticks[i]));
                        i++;
                    }

                    //load the existing file and merge candlesticks 
                    HistoryChunkId newChunkId = idFactory(this.Id, startDate, endDate);
                    var fileToLoad = newChunkId.GetFilePath(dataDir);
                    if (File.Exists(fileToLoad))
                    {
                        var loadedChunk = HistoryChunk.Load(fileToLoad).Result;
                        foreach (var candle in loadedChunk.Ticks)
                            if (candle.Time > startDate && candle.OpenTime < endDate)
                                candlesOfMont.AddRecord(candle, true);
                    }
                    HistoryChunk dataToSave = chunkFactory(newChunkId, candlesOfMont.ToList());

                    dataToSave.SaveAsync(dataDir).Wait();

                    this.LoadedFiles.Add(newChunkId);
                }
            }
        }




        public void Save_daily(
            string dataDir,
            Func<SymbolHistoryId, DateTime, DateTime, HistoryChunkId> idFactory,
            Func<HistoryChunkId, List<Candlestick>, HistoryChunk> chunkFactory)
        {
            lock (_Ticks)
            {
                int i = 0;
                while (i < this.Ticks.Count)
                {
                    var curTick = this.Ticks[i];
                    DateTime startDate = new DateTime(curTick.Time.Year, curTick.Time.Month, curTick.Time.Day, 0, 0, 0, DateTimeKind.Utc);
                    DateTime endDate = startDate.AddDays(1);
                    TimeSerie<Candlestick> chunkBars = new TimeSerie<Candlestick>();


                    //todo load all the chunks that overlap this period
                    //todo delete loaded chunks

                    //load the existing file and merge candlesticks 
                    HistoryChunkId newChunkId = idFactory(this.Id, startDate, endDate);
                    var fileToLoad = newChunkId.GetFilePath(dataDir);
                    if (!LoadedFiles.Contains(newChunkId) && File.Exists(fileToLoad))
                    {
                        var loadedChunk = HistoryChunk.Load(fileToLoad).Result;
                        foreach (var candle in loadedChunk.Ticks)
                            if (candle.Time > startDate && candle.OpenTime < endDate)
                                chunkBars.AddRecord(candle, true);
                    }


                    //add bars from this view
                    while (i < this.Ticks.Count && this.Ticks[i].OpenTime < endDate)
                    {
                        chunkBars.AddRecord(new Candlestick(this.Ticks[i]));
                        i++;
                    }

                    //finally save data 
                    HistoryChunk dataToSave = chunkFactory(newChunkId, chunkBars.ToList());

                    dataToSave.SaveAsync(dataDir).Wait();

                    this.LoadedFiles.Add(dataToSave.ChunkId);
                }
            }
        }
    }
}
