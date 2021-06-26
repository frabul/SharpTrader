using System.Collections.Generic;

namespace SharpTrader.Charts
{
    public class CandlestickSeries : ChartSeries
    {
        public override ChartSeriesType Type => ChartSeriesType.Candlestick;
        public List<ChartCandlestick> Points { get; set; } = new List<ChartCandlestick>();
    }
}
