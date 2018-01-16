using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTrader.Tests;
namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        { 
            //------
            TestMarketSimulator tms = new TestMarketSimulator();
            tms.Test();
        }
    }
}
