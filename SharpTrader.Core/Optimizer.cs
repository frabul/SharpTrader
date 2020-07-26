using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpTrader.Storage;
using System;
using System.IO;
using System.Linq;

namespace SharpTrader
{
    class OptimizerSession
    {
        public int Lastexecuted { get; set; }
    }


    public class Optimizer
    {
        TradeBarsRepository HistoryDB ;
        public class Configuration
        {
            public string SessionName;
            public BackTester.Configuration BacktesterConfig;
        }

        private string SessionFile => $"OptimizerSession_{SessionName}.json";
        private string SpaceFile => $"OptimizerSession_{SessionName}_space.json";

        private OptimizerSession Session;
        private NLog.Logger Logger;
        private OptimizationSpace BaseSpace;

        public Configuration Config { get; }
        public string SessionName => Config.SessionName;
        public Action<PlotHelper> ShowPlotCallback { get; set; }
        public Optimizer(Configuration config, Action<PlotHelper> showPlotCallback)
        {
            ShowPlotCallback = showPlotCallback;
            Config = config;
            //load session
            var sessionJson = File.ReadAllText(SessionFile);
            Session = JsonConvert.DeserializeObject<OptimizerSession>(sessionJson);
            //load space
            var spaceJson = File.ReadAllText(SpaceFile);
            BaseSpace = OptimizationSpace.FromJson(spaceJson);
        }

        public void Start()
        {
            Logger = NLog.LogManager.GetLogger($"Optimizer_{SessionName}");
            Logger.Info($"Starting optimization session: {SessionName}");
            HistoryDB = new TradeBarsRepository(Config.BacktesterConfig.HistoryDb);
            for (int i = Session.Lastexecuted + 1; i < BaseSpace.Configurations.Count; i++)
            {
                RunOne(BaseSpace.Configurations[i]);
                Session.Lastexecuted = i;
                File.WriteAllText(SessionFile, JsonConvert.SerializeObject(Session));
            }

            //Parallel.ForEach(paramSets, new ParallelOptions { MaxDegreeOfParallelism = 2 }, act);
        }

        void RunOne(object algoConfig)
        {
            var backtesterConfig = JsonConvert.DeserializeObject<BackTester.Configuration>(JsonConvert.SerializeObject(Config.BacktesterConfig));
            backtesterConfig.AlgoConfig = JObject.FromObject(algoConfig);
            var backTester = new BackTester(backtesterConfig, HistoryDB)
            {
                ShowPlotCallback = ShowPlotCallback,
                Logger = this.Logger, 
                
            };
            backTester.Start(); 
        }
    }
}
