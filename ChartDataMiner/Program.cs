
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.API.Csharp.Client;
using Binance.API.Csharp.Client.Models.Market;

namespace ChartDataMiner
{
    class Program
    {
        static void Main(string[] args)
        {
            BinanceDataMiner dm = new BinanceDataMiner(".\\Data\\");
            dm.CreateSymbolsTable();
            dm.MineBinance();

        }

    }
}
