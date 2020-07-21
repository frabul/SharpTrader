using System;
using System.Collections.Generic;
using System.Diagnostics;
using LiteDB;

namespace SharpTrader.Storage
{
    public class SymbolHistoryMetaData
    {
        [BsonId]
        public string Id { get; protected set; }
        public SymbolHistoryId Info { get; protected set; }
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
        public SymbolHistoryRawExt Ticks { get; set; }
        [BsonIgnore]
        public object Locker { get; } = new object();

        public List<HistoryFileInfo> Chunks { get; set; }
       

        public SymbolHistoryMetaDataInternal(SymbolHistoryId histInfo)
        {
            Info = histInfo;
            Id = histInfo.GetKey();
            Chunks = new List<HistoryFileInfo>();
            GapsConfirmed = new List<DateRange>();
            GapsUnconfirmed = new List<DateRange>();
            Ticks = new SymbolHistoryRawExt
            {
                Ticks = new List<Candlestick>(),
                FileName = histInfo.GetKey(),
                Market = histInfo.Market,
                Spread = 0,
                Symbol = histInfo.Symbol,
                Timeframe = histInfo.Timeframe
            };

        }
        /// <summary>
        /// Constructor used by serialization
        /// </summary>
        public SymbolHistoryMetaDataInternal( )
        {

        }
        public void UpdateBars(ITradeBar bar)
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
                var data = this.Ticks;
                foreach (var c in candles)
                {
                    this.UpdateBars(c);
                    data.UpdateBars(c);
                    ITradeBar lastCandle = data.Ticks.Count > 0 ? data.Ticks[data.Ticks.Count - 1] : null;
                    if (c.Timeframe != data.Timeframe)
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
                            int i = data.Ticks.BinarySearch(toAdd, CandlestickTimeComparer.Instance);
                            int index = i;
                            if (i > -1)
                                data.Ticks[index] = toAdd;
                            else
                            {
                                index = ~i;
                                data.Ticks.Insert(index, toAdd);
                            }
                            if (index > 0)
                                Debug.Assert(data.Ticks[index].OpenTime >= data.Ticks[index - 1].OpenTime);
                            if (index + 1 < data.Ticks.Count)
                                Debug.Assert(data.Ticks[index].OpenTime <= data.Ticks[index + 1].OpenTime);

                        }
                        else
                            data.Ticks.Add(toAdd);
                    }
                }
            }
        }

    }
}
