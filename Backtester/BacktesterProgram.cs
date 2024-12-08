using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using SharpTrader;
using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BacktesterProgram
{
    class BacktesterProgram
    {
       
        static void ShowPlot(PlotHelper plot)
        {
            //todo implement html plots
        }
        static Type dummy;
        static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var logger = new Serilog.LoggerConfiguration()
                .ReadFrom.Configuration(configuration) 
                .Destructure.ToMaximumDepth(1)
                .CreateLogger();

            if (args.Length < 1)
            {
                dummy = typeof(SharpTrader.Algos.HighPassMeanReversionAlgo2);
                throw new Exception("Missing argument: config file (json)");
            }

            var json = File.ReadAllText(args[0]);
            var config = JsonConvert.DeserializeObject<BackTester.Configuration>(json);
            BackTester tester = new BackTester(config);
            tester.ShowPlotCallback = ShowPlot;
            tester.Start();
            Console.ReadLine();
        }
    }
}
