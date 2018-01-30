using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core
{
    public class BackTester
    {
        MultiMarketSimulator Simulator;
        TraderBot[] _Bots;
        public bool Started { get; private set; }

        public TraderBot[] Bots
        {
            get => _Bots;
            set
            {
                if (Started) 
                    throw new InvalidOperationException("You can set bots only before starting backtest"); 
                _Bots = value;
            }
        }

        public BackTester(MultiMarketSimulator simulator)
        {
            Simulator = simulator;
        }

        public void Start(DateTime simStart, DateTime simEnd)
        {
            if (Started)
                return; 
            Started = true;

            foreach (var bot in Bots)
                bot.Start();


            bool raiseEvents = false;
            int steps = 1;
            decimal MaxDrawDown = 0;
            decimal BalancePeak = 0;
            decimal MaxDDPrc = 0;

            while (Simulator.NextTick(raiseEvents) && Simulator.Time < simEnd)
            { 
                raiseEvents = simStart <= Simulator.Time;
                if (steps % 240 == 0 && raiseEvents)
                {
                     //todo add some callback
                }
                steps++; 
                var currentBalance = this.Simulator.Markets.Sum(m => m.GetBtcPortfolioValue());
                BalancePeak = currentBalance > BalancePeak ? currentBalance : BalancePeak;
                if (BalancePeak - currentBalance > MaxDrawDown)
                {
                    MaxDrawDown = BalancePeak - currentBalance;
                    MaxDDPrc = MaxDrawDown / BalancePeak;
                }
            } 
        }

    }

    class RobotOperation
    {
        public List<ITrade> Trades { get; set; }
        public DateTime TimeStart { get; set; }
        public DateTime TimeEnd { get; set; }
    }
}
