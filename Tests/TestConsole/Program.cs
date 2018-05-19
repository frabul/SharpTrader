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
            var histDb = new HistoricalRateDataBase(".\\Data13\\");

           var api = new BinanceMarketApi(
                   "SHyGhmGPo7qsfQB0yF5yi48pIqV1VBTW22cDtOFzYwl3p7MeekHy3rU39bNo9L8C",
                   "yUgV0MMKcqI6HfEvONap71IODQs151JyFLQChh34NC4gmZ5Cj0d7wzJ8qdcrH202", histDb);
    


            LiteDatabase db = new LiteDatabase(@".\MyData.db");
            var coll = db.GetCollection<test>();
            coll.EnsureIndex("Symbol");
        
             



            db = new LiteDatabase(@".\MyData.db");


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
