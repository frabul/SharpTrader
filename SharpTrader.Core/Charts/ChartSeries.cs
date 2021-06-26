using System.Collections.Generic;

namespace SharpTrader.Charts
{
    public abstract class ChartSeries
    {
        public abstract ChartSeriesType Type { get; }
        public string Name { get; set; }
        public string PriceScaleId { get; set; }
        public SeriesOptions Options { get; set; }
        public List<SeriesMarker> Markers { get; set; } = new List<SeriesMarker>(); 
    }
}
