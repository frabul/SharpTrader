using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using ProtoBuf;

namespace SharpTrader.Storage
{
    class HistoryView
    {
        Logger Logger = LogManager.GetLogger("HistoryView");
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

        /// <summary>
        /// Saves all data to disk and updates LoadedFiles list
        /// </summary>
        /// <param name="dataDir"></param>
        public void Save_Protobuf(string dataDir)
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
                    HistoryChunkId newChunkId = new HistoryChunkIdV2(this.Id, startDate);
                    var fileToLoad = newChunkId.GetFilePath(dataDir);
                    if (File.Exists(fileToLoad))
                    {
                        var loadedChunk = HistoryChunk.Load(fileToLoad);
                        foreach (var candle in loadedChunk.Ticks)
                            if (candle.Time >= startDate && candle.OpenTime <= endDate)
                                candlesOfMont.AddRecord(candle, true);
                    }


                    //finally save data 
                    HistoryChunk dataToSave = new HistoryChunk()
                    {
                        ChunkId = newChunkId,
                        Ticks = candlesOfMont.ToList(),
                    };
                    using (var fs = File.Open(newChunkId.GetFilePath(dataDir), FileMode.Create))
                        Serializer.Serialize<HistoryChunk>(fs, dataToSave);

                    this.LoadedFiles.Add(newChunkId);
                }
            }
        }
    }
}
