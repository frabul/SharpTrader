using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response;
using BinanceExchange.API.Models.Response.Error;
using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader.Core.BrokersApi.Binance
{
    public class BinanceTradeBarsRepository : TradeBarsRepository
    {
        Serilog.ILogger Logger = Serilog.Log.ForContext<BinanceTradeBarsRepository>();
        private BinanceClient Client;
        private Dictionary<string, SemaphoreSlim> Semaphores = new Dictionary<string, SemaphoreSlim>();

        private SemaphoreSlim DownloadCandlesSemaphore;
        public int ConcurrencyCount { get; set; } = 10;

        public BinanceTradeBarsRepository(string dataDir, BinanceClient cli, ChunkFileVersion cv = ChunkFileVersion.V3, ChunkSpan chunkSpan = ChunkSpan.OneDay) : base(dataDir, cv, chunkSpan)
        {
            Client = cli;
            DownloadCandlesSemaphore = new SemaphoreSlim(ConcurrencyCount, ConcurrencyCount);
        }

        //--------------------------------------------
        public BinanceTradeBarsRepository(string dataDir, ChunkFileVersion cv = ChunkFileVersion.V3, ChunkSpan chunkSpan = ChunkSpan.OneDay, double rateLimitFactor = 0.4f) : base(dataDir, cv, chunkSpan)
        {
            Client = new BinanceClient(new ClientConfiguration { ApiKey = "asd", SecretKey = "asd", EnableRateLimiting = false, RateLimitFactor = rateLimitFactor });
            DownloadCandlesSemaphore = new SemaphoreSlim(ConcurrencyCount, ConcurrencyCount);
        }

        public async Task SynchSymbolsTableAsync(string DataDir)
        {
            Dictionary<string, BinanceSymbolInfo> dict = new Dictionary<string, BinanceSymbolInfo>();
            var tradingRules = await Client.GetExchangeInfo();
            foreach (var symb in tradingRules.Symbols)
            {
                dict.Add(symb.symbol, new BinanceSymbolInfo(symb));
            }
            var crossPairs = await Client.GetAllCrossMarginPairs();
            foreach (var pair in crossPairs)
            {
                if (dict.ContainsKey(pair.symbol))
                    dict[pair.symbol].IsCrossMarginAllowed = true;
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            System.IO.File.WriteAllText(System.IO.Path.Combine(DataDir, "BinanceSymbolsTable.json"), json);
        }

        public async Task AssureData(SymbolHistoryId histInfo, DateTime fromTime, DateTime toTime)
        {


            if (toTime > DateTime.Now.AddYears(10))
                toTime = DateTime.Now.AddYears(10);
            toTime = new DateTime(toTime.Year, toTime.Month, toTime.Day, toTime.Hour, toTime.Minute, 0, DateTimeKind.Utc);
            bool reported = false;
            var epoch = new DateTime(2017, 07, 01, 0, 0, 0, DateTimeKind.Utc);
            if (fromTime < epoch)
                fromTime = epoch;



            await DownloadCandlesSemaphore.WaitAsync();
            var sem = GetSemaphore(histInfo.Symbol);
            try
            {
                await sem.WaitAsync();
                var hist = this.GetSymbolHistory(histInfo, fromTime, toTime);
                var oldTicks = hist.Ticks;

                //first find next available data, if not found download everything 
                DateTime checkTime = fromTime;
                while (checkTime < toTime)
                {
                    void ReportAssure(DateTime holeEndTime)
                    {
                        if (!reported)
                            Logger.Information("Assuring data for {Symbol} from {FromTime:yyyyMMdd HH:mm} to {ToTime:yyyyMMdd HH:mm}.", histInfo.Symbol, fromTime, toTime);
                        Logger.Debug("{Symbol} - Hole found in history from {FromTime} to {ToTime}.", histInfo.Symbol, checkTime, holeEndTime);
                        reported = true;
                    }

                    if (oldTicks.MoveNext())
                    {
                        if (oldTicks.Time - checkTime > histInfo.Resolution)
                        {
                            ReportAssure(oldTicks.Current.CloseTime);
                            var candles = await DownloadCandles(histInfo.Symbol, checkTime, oldTicks.Current.CloseTime);
                            this.AddCandlesticks(histInfo, candles);
                        }
                        checkTime = oldTicks.Current.CloseTime;
                    }
                    else
                    {
                        if (oldTicks.Count < 1 || oldTicks.Current.CloseTime < checkTime)
                        {
                            ReportAssure(toTime);
                            //there isn't any other tick, all remaining data needs to be downloaded 
                            var candles = await DownloadCandles(histInfo.Symbol, checkTime, toTime);
                            this.AddCandlesticks(histInfo, candles);
                            checkTime = toTime;
                        }
                        else
                            checkTime = checkTime + histInfo.Resolution;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "{Symbol} - Fatal Exception while download history", histInfo.Symbol);
            }
            finally
            {
                DownloadCandlesSemaphore.Release();
                sem.Release();
            }

        }

        private async Task<List<Candlestick>> DownloadCandles(string symbol, DateTime startTime, DateTime endTime)
        {
            Logger.Debug("{Symbol} - Downloading candles for  from {FromTime} to {ToTime}", symbol, startTime, endTime);
            List<Candlestick> allCandles = new List<SharpTrader.Candlestick>();
            try
            {
                bool noMoreData = false;
                int zeroCount = 0;

                while (!noMoreData && (allCandles.Count < 1 || allCandles.Last().CloseTime < endTime))
                {
                    //Console.WriteLine($"Downloading history for {symbol} - {startTime}");
                    try
                    {
                        var candles = await Client.GetKlinesCandlesticks(new GetKlinesCandlesticksRequest
                        {
                            Symbol = symbol,
                            StartTime = startTime - TimeSpan.FromSeconds(60),
                            Interval = KlineInterval.OneMinute,
                            EndTime = endTime - TimeSpan.FromSeconds(60),
                        });

                        var batch = candles.Select(KlineToCandlestick).ToList();

                        allCandles.AddRange(batch);

                        //if we get no data for at least 2 requests then we can assume that there isn't any more data
                        if (batch.Count < 1)
                            zeroCount++;
                        else
                            zeroCount = 0;
                        if (zeroCount > 1)
                            noMoreData = true;
                    }
                    catch (Exception ex)
                    {
                        var msg = $"Exception during {symbol} history download: ";
                        if (ex is BinanceException binException)
                            msg += binException.ErrorDetails;
                        else
                            msg += ex.Message;
                        Console.WriteLine(msg);
                        await Task.Delay(3000);
                    }
                    if (allCandles.Count > 1)
                        startTime = new DateTime((allCandles[allCandles.Count - 1].CloseTime.AddSeconds(1)).Ticks, DateTimeKind.Utc);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "{Symbol} - Fatal error during download of klines ", symbol);
            }
            return allCandles;
        }

        private static Candlestick KlineToCandlestick(KlineCandleStickResponse c)
        {
            return new SharpTrader.Candlestick()
            {
                Open = (double)c.Open,
                High = (double)c.High,
                Low = (double)c.Low,
                Close = (double)c.Close,
                OpenTime = c.OpenTime,
                CloseTime = c.OpenTime.AddSeconds(60), //+ c.CloseTime.AddMilliseconds(1),
                QuoteAssetVolume = (double)c.QuoteAssetVolume
            };

        }

        private SemaphoreSlim GetSemaphore(string symbol)
        {
            if (!Semaphores.ContainsKey(symbol))
                Semaphores.Add(symbol, new SemaphoreSlim(1, 1));
            return Semaphores[symbol];
        }

        public override ISymbolHistory GetSymbolHistory(SymbolHistoryId info, DateTime startOfData, DateTime endOfData)
        {
            return base.GetSymbolHistory(info, startOfData, endOfData);
        }

        public async Task AssureFilter(Func<string, bool> filter, DateTime fromTime, DateTime toTime)
        {
            var exchangeInfo = Client.GetExchangeInfo().Result;
            var symbols = exchangeInfo.Symbols;

            var toDownload = symbols

                .Where(s => filter(s.symbol))
                .Select(sp => sp.symbol).ToList();
            toDownload.Sort();
            List<Task> tasks = new List<Task>();
            foreach (var sym in toDownload)
            {
                var histInfo = new SymbolHistoryId("Binance", sym, TimeSpan.FromMinutes(1));
                var task = this.AssureData(histInfo, fromTime, toTime).ContinueWith(t => this.SaveAndClose(histInfo, true));
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }



    }
}
