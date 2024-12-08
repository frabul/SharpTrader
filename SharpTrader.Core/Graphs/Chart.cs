using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpTrader.Drawing
{
    public enum SeriesMarkerPosition { aboveBar, belowBar, inBar }
    public enum SeriesMarkerShape { circle, square, arrowUp, arrowDown }
    public class Figure
    {
        public string Name { get; set; }
        public decimal WidthRelative { get; set; } = 1;
        public decimal HeightRelative { get; set; } = 1;
        public List<Series> Series { get; set; } = new List<Series>();
        public List<PriceLine> HorizontalLines { get; set; } = new List<PriceLine>();

        internal void AddSeries(Series series)
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
            var points = values.Select(v => new Point(v.Time, (decimal)v.Value)).ToList();

            var options = new SeriesOptions()
            {
                color = color,
                lineWidth = lineWidth,
                priceScaleId = axis,
                style = style,
                margins = margins,
            };
            var chartLine = new Line() { Points = points, Options = options };
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

    public abstract class Series
    {
        public abstract SeriesType Type { get; }
        public string Name { get; set; }
        public string PriceScaleId { get; set; }
        public SeriesOptions Options { get; set; }
        public List<SeriesMarker> Markers { get; set; } = new List<SeriesMarker>();


    }

    public enum LineStyle
    {
        Solid,
        Dotted,
        Dashed,
        LargeDashed,
        SparseDotted
    }

    public class PriceLine
    {
        public decimal Price { get; set; }
        public ColorARGB Color { get; set; }
        public int LineWidth { get; set; } = 1;
        public LineStyle Style { get; set; } = LineStyle.Solid;
    }

    public class CandlestickSeries : Series
    {
        public override SeriesType Type => SeriesType.Candlestick;
        public List<ChartCandlestick> Points { get; set; } = new List<ChartCandlestick>();
    }

    public struct Point
    {
        public Point(DateTime x, decimal y) { time = x; value = y; }
        public DateTime time { get; set; }
        public decimal value { get; set; }
    }
    public class Margins { public int above; public int below; }
    public class SeriesOptions
    {

        public ColorARGB color { get; set; } = ARGBColors.Black;
        public int lineWidth { get; set; } = 1;
        public LineStyle style { get; set; } = LineStyle.Solid;
        public Margins margins { get; set; }
        public string priceScaleId { get; set; }
    }
    public class Line : Series
    {
        public override SeriesType Type => SeriesType.Line;
        public List<Point> Points { get; set; } = new List<Point>();
    }

    public class Histogram : Series
    {
        public override SeriesType Type => SeriesType.Histogram;
    }

    public enum SeriesType
    {
        Candlestick,
        Line,
        Histogram,
        Bar,
        Area
    }

    public struct ChartCandlestick
    {
        public DateTime time;
        public decimal open;
        public decimal high;
        public decimal low;
        public decimal close;

        public ChartCandlestick(ITradeBar toCopy) : this()
        {
            open = (decimal)toCopy.Open;
            close = (decimal)toCopy.Close;
            high = (decimal)toCopy.High;
            low = (decimal)toCopy.Low;
            time = toCopy.CloseTime;

        }
    }

    public class Chart
    {
        public List<Figure> Figures { get; set; } = new List<Figure>();

        public Chart()
        {

        }

        public void PlotHorizontalLine()
        {

        }

        public void Serialize(string filePath)
        {
            using var fs = File.Open(filePath, FileMode.Create);
            using var tw = new StreamWriter(fs);
            var serializer = new JsonSerializer();
            serializer.Formatting = Formatting.Indented;
            serializer.NullValueHandling = NullValueHandling.Ignore;
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Converters.Add(new ChartPointDateTimeConverter());
            serializer.Converters.Add(new ColorConvertelr());
            serializer.Serialize(tw, this);

        }

        public Figure NewFigure()
        {
            var fig = new Figure();
            this.Figures.Add(fig);
            return fig;
        }

        public class ChartPointDateTimeConverter : JsonConverter<DateTime>
        {
            public override bool CanWrite => base.CanWrite;
            public override bool CanRead => false;
            public override DateTime ReadJson(JsonReader reader, Type objectType, [AllowNull] DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override void WriteJson(JsonWriter writer, [AllowNull] DateTime value, JsonSerializer serializer)
            {
                writer.WriteValue(value.ToEpoch());
            }
        }

        public class ColorConvertelr : JsonConverter<ColorARGB>
        {
            public override bool CanWrite => true;
            public override bool CanRead => false;
            public override ColorARGB ReadJson(JsonReader reader, Type objectType, [AllowNull] ColorARGB existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override void WriteJson(JsonWriter writer, [AllowNull] ColorARGB value, JsonSerializer serializer)
            {
                writer.WriteValue(value.ToString("#"));
            }
        }
    }

    public class SeriesMarker
    {
        public DateTime time { get; }
        public ColorARGB color { get; }
        public SeriesMarkerPosition position { get; }
        public SeriesMarkerShape shape { get; }
        public int size { get; }
        public string text { get; }

        public SeriesMarker(DateTime time, ColorARGB color, SeriesMarkerPosition position, SeriesMarkerShape shape, int size, string text)
        {
            this.time = time;
            this.color = color;
            this.position = position;
            this.shape = shape;
            this.size = size;
            this.text = text;
        }

        public override bool Equals(object obj)
        {
            return obj is SeriesMarker other &&
                   time == other.time &&
                   EqualityComparer<ColorARGB>.Default.Equals(color, other.color) &&
                   position == other.position &&
                   shape == other.shape &&
                   size == other.size &&
                   text == other.text;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(time, color, position, shape, size, text);
        }
    }
}
