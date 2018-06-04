using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpTrader;
using SharpTrader.Tests;
using LiteDB;
namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var histDb = new HistoricalRateDataBase(".\\Data\\");

          

            TestMarketSimulator tms = new TestMarketSimulator();
            tms.Test();
        }

        public class test
        {
            public string Symbol { get; set; }
            public LiteCollection<string> Operations { get; set; }
        }
    }
}
