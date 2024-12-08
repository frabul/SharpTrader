using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB;
using NLog;

namespace SharpTrader.Storage
{
    public class SymbolHistoryMetaData
    {
        [BsonId]
        public string Id { get; protected set; }
        public SymbolHistoryId HistoryId { get; protected set; }
        public List<DateRange> GapsConfirmed { get; protected set; }
        public List<DateRange> GapsUnconfirmed { get; protected set; }
        /// <summary>
        /// First absolute data for this symbol
        /// </summary>
        public ITradeBar FirstKnownData { get; internal set; }
        /// <summary>
        /// First bar recorded in database
        /// </summary>
        public ITradeBar FirstBar { get; protected set; }
        /// <summary>
        /// Last bar recorded in database
        /// </summary>
        public ITradeBar LastBar { get; protected set; }
    }

    public class SymbolHistoryMetaDataInternal : SymbolHistoryMetaData
    {
        [BsonIgnore]
        private object Locker = new object();

        private HistoryView _View;
        [BsonIgnore]
        private Logger Logger = LogManager.GetLogger("SymbolHistoryMetaDataInternal");

        [BsonIgnore]
        internal HistoryView View
        {
            get
            {
                if (_View == null)
                    _View = new HistoryView(HistoryId);
                return _View;
            }
            private set { _View = value; }
        }

        public HashSet<HistoryChunkId> Chunks { get; set; }
        public bool Validated { get; internal set; } = false;

        public SymbolHistoryMetaDataInternal(SymbolHistoryId historyId)
        {
            HistoryId = historyId;
            Id = historyId.Key;
            Chunks = new HashSet<HistoryChunkId>();
            GapsConfirmed = new List<DateRange>();
            GapsUnconfirmed = new List<DateRange>();
            View = new HistoryView(historyId);

        }
        /// <summary>
        /// Constructor used by serialization
        /// </summary>
        [BsonCtor]
        public SymbolHistoryMetaDataInternal()
        {

        }

        public void UpdateLastBar(ITradeBar bar)
        {
            if (LastBar == null || this.LastBar.Time < bar.Time)
                this.LastBar = bar;

            if (FirstBar == null || this.FirstBar.Time > bar.Time)
                this.FirstBar = bar;
        }

        public void AddBars(IEnumerable<Candlestick> candles)
        {
            lock (this.Locker)
            {
                foreach (var c in candles)
                {
                    this.UpdateLastBar(c);
                    View.AddBar(c);
                }
            }
        }

        internal void Flush(string dataDir)
        {
            HistoryView oldView;
            lock (this.Locker)
            {
                oldView = this.View;
                View = new HistoryView(View.Id);
            }

            //save the view
            if (oldView != null)
            {
                oldView.Save_Protobuf(dataDir);
                lock (this.Locker)
                {
                    //check that saved chucks are present in chunks cache
                    foreach (var chunk in oldView.LoadedFiles)
                        this.Chunks.Add(chunk);
                }
            }
        }

        public void LoadHistory(string dataDir, DateTime startOfData, DateTime endOfData)
        {
            startOfData = new DateTime(startOfData.Year, startOfData.Month, startOfData.Day, 0, 0, 0, DateTimeKind.Utc);
            List<DateRange> missingData = new List<DateRange>();
            lock (this.Locker)
            {
                if (this.View == null)
                {
                    this.View = new HistoryView(this.HistoryId);
                }
                //check if we already have some records and load them   
                if (this.View.TicksCount < 1)
                {
                    missingData.Add(new DateRange(startOfData, endOfData));
                }
                else
                {
                    if (this.View.StartOfData > startOfData)
                    {
                        missingData.Add(new DateRange(startOfData, this.View.StartOfData));
                    }
                    if (endOfData > this.View.EndOfData)
                    {
                        missingData.Add(new DateRange(this.View.EndOfData, endOfData));
                    }
                }

                //load missing data  
                foreach (var finfo in this.Chunks)
                {
                    Debug.Assert(HistoryId.Key == finfo.HistoryId.Key, $"Hist id {HistoryId.Key} - finfo {finfo.HistoryId.Key}");
                    var dateInRange = missingData.Any(dr =>
                        (dr.start <= finfo.StartDate && dr.end >= finfo.StartDate) ||
                        (dr.start >= finfo.StartDate && dr.end <= finfo.EndDate) ||
                        (dr.start <= finfo.EndDate && dr.end >= finfo.EndDate)
                        );
                    if (dateInRange && this.View.LoadedFiles.Add(finfo)) //if is in any range and not already loaded
                    {
                        try
                        {

                            HistoryChunk fdata = HistoryChunk.Load(finfo.GetFilePath(dataDir));
                            this.AddBars(fdata.Ticks.Where(tick => tick.Time >= startOfData && tick.Time <= endOfData));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"ERROR loading file {finfo.GetFilePath(dataDir)}: {ex.Message}");
                        }
                    }
                }
            }

        }

        public void ClearView()
        {
            View = new HistoryView(View.Id);
        }
    }
}
