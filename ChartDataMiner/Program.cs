
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader.Utils;

namespace ChartDataMiner
{
    class Program
    {
        static void Main(string[] args)
        {
            BinanceDataDownloader dm = new BinanceDataDownloader(".\\Data\\");
            dm.SynchSymbolsTable(".\\Data\\");
            dm.MineBinance();

        }

    }
}
