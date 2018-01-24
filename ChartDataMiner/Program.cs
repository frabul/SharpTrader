
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.API.Csharp.Client;
using Binance.API.Csharp.Client.Models.Market;
using SharpTrader.Utils;

namespace ChartDataMiner
{
    class Program
    {
        static void Main(string[] args)
        {
            BinanceDataDownloader dm = new BinanceDataDownloader(".\\Data\\");
            dm.SynchSymbolsTable();
            dm.MineBinance();

        }

    }
}
