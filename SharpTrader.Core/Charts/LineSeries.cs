using System.Collections.Generic;

namespace SharpTrader.Charts
{
    public class LineSeries : ChartSeries
    {
        public override ChartSeriesType Type => ChartSeriesType.Line;
        public List<ChartPoint> Points { get; set; } = new List<ChartPoint>();
    }
}
