using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NLog;
using ProtoBuf;

namespace SharpTrader.Storage
{
    class HistoryView
    {
        Logger Logger = LogManager.GetCurrentClassLogger();
        private List<Candlestick> _Ticks;
        public SymbolHistoryId Id { get; set; }
        public HashSet<HistoryChunkId> LoadedFiles { get; set; } = new HashSet<HistoryChunkId>();
        public DateTime StartOfData { get; set; } = DateTime.MaxValue;
        public DateTime EndOfData { get; set; } = DateTime.MinValue;

        public HistoryView(SymbolHistoryId id)
        {
            Id = id;
            _Ticks = new List<Candlestick>();
        }

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
                if (lastCandle?.OpenTime > toAdd.OpenTime)
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
                        Debug.Assert(this.Ticks[index].OpenTime >= this.Ticks[index - 1].OpenTime);
                    if (index + 1 < this.Ticks.Count)
                        Debug.Assert(this.Ticks[index].OpenTime <= this.Ticks[index + 1].OpenTime);
                }
                else
                    this.Ticks.Add(toAdd);
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
                    List<Candlestick> candlesOfMont = new List<Candlestick>();
                    while (i < this.Ticks.Count && this.Ticks[i].OpenTime < endDate)
                    {
                        candlesOfMont.Add(new Candlestick(this.Ticks[i]));
                        i++;
                    }

                    HistoryChunkId newChunkId = new HistoryChunkId(this.Id, startDate);
                    HistoryChunk sdata = new HistoryChunk()
                    {
                        ChunkId = newChunkId,
                        Ticks = candlesOfMont,
                    };
                    using (var fs = File.Open(newChunkId.GetFilePath(dataDir), FileMode.Create))
                        Serializer.Serialize<HistoryChunk>(fs, sdata);

                    this.LoadedFiles.Add(newChunkId);
                }
            }
        }
    }
}
