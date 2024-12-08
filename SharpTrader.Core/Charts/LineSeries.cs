using System.Collections.Generic;
using System.Linq;

namespace SharpTrader.Charts
{
    public class LineSeries : ChartSeries
    {
        public override ChartSeriesType Type => ChartSeriesType.Line;
        public List<ChartPoint> Points { get; set; } = new List<ChartPoint>();

        public LineSeries() { }

        public LineSeries(IEnumerable<ChartPoint> chartPoints, SeriesOptions options)
        {
            Points = new List<ChartPoint>(chartPoints.OrderBy(p => p.time));
            Options = options;
        }


    }
}
