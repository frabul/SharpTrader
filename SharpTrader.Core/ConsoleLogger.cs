using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class ConsoleLogger : ILogger
    {
        public void LogInfo(string v)
        {
            Console.WriteLine(v);
        }
    }
}
