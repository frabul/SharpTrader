using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessagePack;

namespace SharpTrader.Storage
{
    /// <summary>
    /// Data contained is > startDate and  <= endDate
    /// </summary>

    public class ChunkId_BinancePublic : HistoryChunkId
    {

        private DateTime startDate;

        private DateTime endDate;


        public override SymbolHistoryId HistoryId { get; set; }


        public override DateTime StartDate { get => startDate; set => startDate = value.ToUniversalTime(); }


        public override DateTime EndDate { get => endDate; set => endDate = value.ToUniversalTime(); }


        public override string Key => HistoryId.Key + $"_{StartDate:yyyyMMddHHmmss}_{EndDate:yyyyMMddHHmmss}";

        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public ChunkId_BinancePublic()
        {

        }

        public ChunkId_BinancePublic(SymbolHistoryId id, DateTime date, DateTime endDate)
        {
            this.HistoryId = id;
            this.StartDate = date;
            this.endDate = endDate;
        }

        public ChunkId_BinancePublic(HistoryChunkId chunkId)
        {
            this.HistoryId = chunkId.HistoryId;
            this.StartDate = chunkId.StartDate;
            this.endDate = chunkId.EndDate;
        }
        //ADABTC-1m-2021-01
        static Regex FileNameRegex = new Regex(@"([A-Za-z0-9]+)-(1m)-(\d\d\d\d-\d\d)[.]zip", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.CultureInvariant);
        public static new bool TryParse(string filePath, out HistoryChunkId retVal)
        {
            retVal = null;
            try
            {
                var match = FileNameRegex.Match(filePath);
                if (match.Success)
                {
                    var symId = new SymbolHistoryId("Binance", match.Groups[1].Value, TimeSpan.FromMinutes(1));
                    var startDate = DateTime.ParseExact(match.Groups[3].Value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
                    var endDate = startDate.AddMonths(1);
                    retVal = new ChunkId_BinancePublic(symId, startDate, endDate);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception _ex)
            {
                return false;
                //Console.WriteLine($"Error while parsing file info for file {filePath}: {_ex.Message}");
            }

        }

        public override string GetFileName() => $"{HistoryId.Symbol}-1m-{StartDate:yyyy-MM}.zip";
    }

    public class HistoryChunk_BinancePublic : HistoryChunk
    {

        private List<Candlestick> _Ticks;

        public override HistoryChunkId ChunkId { get => Id; set => Id = (ChunkId_BinancePublic)value; }
        public ChunkId_BinancePublic Id { get; set; }
        public override List<Candlestick> Ticks
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

        public HistoryChunk_BinancePublic()
        {
        }

        public override Task SaveAsync(string fileDir)
        {
            throw new NotImplementedException();
        }

        public static async Task<HistoryChunk_BinancePublic> LoadFromAsync(string filePath)
        {
            var fiName = Path.GetFileName(filePath);
            if (ChunkId_BinancePublic.TryParse(fiName, out var id))
            {
                using var file = File.OpenRead(filePath);
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                var allCandles = zip.Entries.SelectMany(entry =>
                {
                    using var stream = entry.Open();
                    return ParseFile(stream);
                });
                return new HistoryChunk_BinancePublic()
                {
                    ChunkId = id,
                    Ticks = allCandles.ToList()
                };
            }
            throw new InvalidOperationException("Invalid file");

        }

        private static List<Candlestick> ParseFile(Stream s)
        {
            StreamReader reader = new StreamReader(s);
            List<Candlestick> result = new List<Candlestick>();
            string line;
            do
            {
                line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var fields = line.Split(',');

                    var candle = new Candlestick()
                    {
                        OpenTime = DateTime.UnixEpoch.AddMilliseconds(long.Parse(fields[0])),
                        //CloseTime = DateTime.UnixEpoch.AddMilliseconds(1 + long.Parse(fields[6])),
                        CloseTime = DateTime.UnixEpoch.AddMilliseconds(long.Parse(fields[0])).AddMinutes(1),
                        Open = double.Parse(fields[1]),
                        High = double.Parse(fields[2]),
                        Low = double.Parse(fields[3]),
                        Close = double.Parse(fields[4]),
                        QuoteAssetVolume = double.Parse(fields[7])
                    };
                    result.Add(candle);
                }
            }
            while (line != null);
            return result;
        }
    }
}
