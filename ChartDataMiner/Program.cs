
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BinanceExchange.API.Models.Response;
using SharpTrader.Utils;

namespace ChartDataMiner
{
    class Program
    {
        static void Main(string[] args)
        {
            BinanceDataDownloader dm = new BinanceDataDownloader(".\\Data\\");
            dm.SynchSymbolsTable(".\\Data\\");

            dm.DownloadSymbols(s => s.QuoteAsset == "ETH");

        }

    }
}
