using Newtonsoft.Json;
using SharpTrader;
using SharpTrader.Indicators;
using SharpTrader.Plotting;
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
            PlottingHelper.Show(plot); 
            Console.ReadLine();
        }

        static void Main(string[] args)
        {
            if(args.Length < 1)
            {
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
