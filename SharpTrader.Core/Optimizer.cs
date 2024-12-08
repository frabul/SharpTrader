﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpTrader.Storage;
using System;
using System.IO;
using System.Linq;

namespace SharpTrader
{
    class OptimizerSession
    {
        public int Lastexecuted { get; set; } = -1;
    }


    public class Optimizer
    {
        TradeBarsRepository HistoryDB;
        public class Configuration
        {
            public string SessionName;
            public BackTester.Configuration BacktesterConfig;
        }

        private string SessionFile => $"OptimizerSession_{SessionName}.json";
        private string SpaceFile => $"OptimizationSapce_{SessionName}.json";

        private OptimizerSession Session;
        private Serilog.ILogger Logger;
        private OptimizationSpace BaseSpace;

        public Configuration Config { get; }
        public string SessionName => Config.SessionName;
        public Action<PlotHelper> ShowPlotCallback { get; set; }
        public Optimizer(Configuration config, Action<PlotHelper> showPlotCallback)
        {
            ShowPlotCallback = showPlotCallback;
            Config = config;
            //load session
            if (File.Exists(SessionFile))
            {
                var sessionJson = File.ReadAllText(SessionFile);
                Session = JsonConvert.DeserializeObject<OptimizerSession>(sessionJson);
            }
            else
                Session = new OptimizerSession();
            //load space
            var spaceJson = File.ReadAllText(SpaceFile);
            BaseSpace = OptimizationSpace.FromJson(spaceJson);
        }

        public void Start()
        {
            Logger = Serilog.Log
                .ForContext<Optimizer>()
                .ForContext("OptimizerSesssion", SessionName);

            Logger.Information("Starting optimization session {OptimizerSesssion}.", SessionName);

            HistoryDB = new TradeBarsRepository(Config.BacktesterConfig.HistoryDb);
            for (int i = Session.Lastexecuted + 1; i < BaseSpace.Configurations.Count; i++)
            {
                Logger.Information("--- Backtesting permutation {Index} of {PermutationsCount} ---", i, BaseSpace.Configurations.Count);
                RunOne(BaseSpace.Configurations[i], i);
                Session.Lastexecuted = i;
                File.WriteAllText(SessionFile, JsonConvert.SerializeObject(Session));
            }
        }

        void RunOne(object algoConfig, int index)
        {
            var backtesterConfig = JsonConvert.DeserializeObject<BackTester.Configuration>(JsonConvert.SerializeObject(Config.BacktesterConfig));
            backtesterConfig.AlgoConfig = JObject.FromObject(algoConfig);
            backtesterConfig.SessionName = $"{this.Config.SessionName}_{index}";
            var backTester = new BackTester(backtesterConfig, HistoryDB, this.Logger);
            backTester.Start();
        }
    }
}
