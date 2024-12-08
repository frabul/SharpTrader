using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpTrader.Charts
{
    public class ChartFigure
    {
        public string Name { get; set; }
        public decimal WidthRelative { get; set; } = 1;
        public decimal HeightRelative { get; set; } = 1;
        public List<ChartSeries> Series { get; set; } = new List<ChartSeries>();
        public List<HorizontalLine> HorizontalLines { get; set; } = new List<HorizontalLine>();

        internal void AddSeries(ChartSeries series)
        {
            Series.Add(series);
        }

        public void AddMarker(DateTime time, ColorARGB color, SeriesMarkerPosition position = SeriesMarkerPosition.aboveBar, SeriesMarkerShape shape = SeriesMarkerShape.circle, int size = 1, string text = null)
        {
            var marker = new SeriesMarker(time, color, position,
                                        shape,
                                        size,
                                        text);
            Series.First().Markers.Add(marker);
        }

        public void PlotLine(IEnumerable<IBaseData> values, ColorARGB color,
                            string axis = null,
                            int lineWidth = 1,
                            LineStyle style = LineStyle.Solid,
                            Margins margins = null)
        {
            var points = values.Select(v => new ChartPoint(v.Time, (decimal)v.Value)).ToList();

            var options = new SeriesOptions()
            {
                color = color,
                lineWidth = lineWidth,
                priceScaleId = axis,
                style = style,
                margins = margins,
            };
            var chartLine = new LineSeries() { Points = points, Options = options };
            this.AddSeries(chartLine);
        }

        public void PlotCandlesticks(string name, IEnumerable<ITradeBar> candles)
        {
            var series = new CandlestickSeries() { Name = name };
            series.Points.AddRange(candles.Select(c => new ChartCandlestick(c)));

            this.AddSeries(series);
        }

        public void PlotCandlesticks(string name, TimeSerieNavigator<ITradeBar> ticks)
        {
            var candles = new CandlestickSeries() { Name = name };
            TimeSerieNavigator<ITradeBar> mySeries = new TimeSerieNavigator<ITradeBar>(ticks);
            while (mySeries.MoveNext())
                candles.Points.Add(new ChartCandlestick(mySeries.Current));
            mySeries.OnNewRecord += rec => candles.Points.Add(new ChartCandlestick(rec));

            this.AddSeries(candles);
        }
    }
}
