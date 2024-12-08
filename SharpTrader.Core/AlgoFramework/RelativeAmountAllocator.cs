using Serilog.Core;
using SharpTrader.AlgoFramework;
using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Core.AlgoFramework
{
    public class RelativeAmountAllocator : FundsAllocator
    {

        public FixedAmountAllocator FixedAllocator { get; private set; }
        /// <summary>
        /// Budget per symbol, relative to total account equity
        /// </summary>
        public decimal RelativeBudgetPerSymbol { get; set; }
        /// <summary>
        /// Total budget, relative to total account equity
        /// </summary>
        public decimal RelativeBudget { get; set; }

        /// <summary>
        /// Budget per operation, relative to total account equity
        /// </summary>
        public AssetAmount RelativeBudgetPerOperation { get; set; }
        EMA<IndicatorDataPoint> EquityAvg { get; set; }
        DateTime NextEquityUpdate { get; set; }
        public TimeSpan EquityUpdateInterval { get; set; } = TimeSpan.FromMinutes(60);

        public TimeSpan CoolDown { get => FixedAllocator.CoolDown; set => FixedAllocator.CoolDown = value; }

        public int MaxActiveOperationsPerSymbol { get => FixedAllocator.MaxActiveOperationsPerSymbol; set => FixedAllocator.MaxActiveOperationsPerSymbol = value; }

        private Serilog.ILogger Logger;

        public RelativeAmountAllocator(AssetAmount relBudgePerOperation)
        {

            EquityAvg = new EMA<IndicatorDataPoint>("EquityAvg", 6);
            RelativeBudgetPerOperation = relBudgePerOperation;
            FixedAllocator = new FixedAmountAllocator
            {
                BudgetPerOperation = new AssetAmount(relBudgePerOperation.Asset, relBudgePerOperation.Amount),
                ProportionalToProfit = false,
                Budget = 0,
                BudgetPerSymbol = 0
            };
        }

        protected override async Task OnInitialize()
        {
            Logger = Algo.Logger.ForContext<RelativeAmountAllocator>();
            NextEquityUpdate = Algo.Time;
            await UpdateEquityAvg();
            await FixedAllocator.Initialize(Algo);
            await base.OnInitialize();
        }

        public override async Task Update(TimeSlice slice)
        {
            await UpdateEquityAvg();
            if (EquityAvg.SamplesCount > 0)
            {
                var eq = (decimal)EquityAvg.Value;
                FixedAllocator.BudgetPerOperation = new AssetAmount(RelativeBudgetPerOperation.Asset, RelativeBudgetPerOperation.Amount * eq);
                FixedAllocator.BudgetPerSymbol = RelativeBudgetPerSymbol * eq;
                FixedAllocator.Budget = RelativeBudget * eq;
            }

            await FixedAllocator.Update(slice);
        }

        private async Task UpdateEquityAvg()
        {
            // Every x hours update the amount 
            if (NextEquityUpdate < DateTime.Now)
            {
                var req = await Algo.Market.GetEquity(this.RelativeBudgetPerOperation.Asset);
                if (req.IsSuccessful)
                {
                    // filter value
                    double eq = (double)req.Result;
                    if (EquityAvg.SamplesCount < 1 || Math.Abs(eq - EquityAvg.Value) < EquityAvg.Value * 0.5)
                    {
                        if (eq != 0)
                            EquityAvg.Update(new IndicatorDataPoint(DateTime.Now, eq));
                    }
                    else
                    {
                        Logger.Error("Equity discarded. {Record}/{EquityAvg}", req.Result, EquityAvg.Value);
                    }
                    // if not ready add again in 5 minutes
                    if (EquityAvg.IsReady)
                        NextEquityUpdate = Algo.Time + EquityUpdateInterval;
                    else
                        NextEquityUpdate = Algo.Time + TimeSpan.FromMinutes(5);
                }
                else
                {
                    // try again in 2 minutes
                    Logger.Error("Error while updating equity  discarded. {Record}/{EquityAvg}", req.Result, EquityAvg.Value);
                    NextEquityUpdate = Algo.Time + TimeSpan.FromMinutes(2);
                }
            }
        }
    }
}
