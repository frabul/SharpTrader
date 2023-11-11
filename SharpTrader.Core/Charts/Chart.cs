using BinanceExchange.API.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using SharpTrader;
using System.Threading;
namespace SharpTrader.Charts
{
#pragma warning disable IDE1006 // Naming Styles
    public enum SeriesMarkerPosition { aboveBar, belowBar, inBar }
    public enum SeriesMarkerShape { circle, square, arrowUp, arrowDown }

    public enum LineStyle
    {
        Solid,
        Dotted,
        Dashed,
        LargeDashed,
        SparseDotted
    }
    public class Margins { public int above; public int below; }

    public class Histogram : ChartSeries
    {
        public override ChartSeriesType Type => ChartSeriesType.Histogram;
    }

    public enum ChartSeriesType
    {
        Candlestick,
        Line,
        Histogram,
        Bar,
        Area
    }

    public class Chart
    {
        public string Name { get; private set; }

        public List<ChartFigure> Figures { get; set; } = new List<ChartFigure>();

        public Chart()
        {

        }

        public Chart(string name)
        {
            this.Name = name;
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
        public static Chart Deserialize(string filePath)
        {
            using var fs = File.Open(filePath, FileMode.Open);
            using var tr = new StreamReader(fs);
            var serializer = new JsonSerializer();
            serializer.Formatting = Formatting.Indented;
            serializer.NullValueHandling = NullValueHandling.Ignore;
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Converters.Add(new ChartPointDateTimeConverter());
            serializer.Converters.Add(new ColorConvertelr());
            serializer.Converters.Add(new ChartSeriesConverter());
            return (Chart)serializer.Deserialize(tr, typeof(Chart));
        }
        public ChartFigure NewFigure()
        {
            var fig = new ChartFigure();
            this.Figures.Add(fig);
            return fig;
        }

        public class ChartPointDateTimeConverter : JsonConverter<DateTime>
        {
            public override bool CanWrite => base.CanWrite;
            public override bool CanRead => true;
            public override DateTime ReadJson(JsonReader reader, Type objectType, [AllowNull] DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                return ((long)reader.Value).ToDatetime();
            }

            public override void WriteJson(JsonWriter writer, [AllowNull] DateTime value, JsonSerializer serializer)
            {
                writer.WriteValue(value.ToEpoch());
            }
        }
        public class ChartSeriesConverter : JsonConverter<ChartSeries>
        {
            public override bool CanWrite => false;
            public override bool CanRead => true;
            public override ChartSeries ReadJson(JsonReader reader, Type objectType, [AllowNull] ChartSeries existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                JObject jo = JObject.Load(reader);
                var type = (ChartSeriesType)Enum.Parse(typeof(ChartSeriesType), jo["Type"].Value<string>());
                ChartSeries series;
                switch (type)
                {
                    case ChartSeriesType.Candlestick:
                        series = new CandlestickSeries();
                        serializer.Populate(jo.CreateReader(), series);
                        break;
                    case ChartSeriesType.Line:
                        series = new LineSeries();
                        serializer.Populate(jo.CreateReader(), series);
                        break;
                    case ChartSeriesType.Histogram:
                        series = new Histogram();
                        serializer.Populate(jo.CreateReader(), series);
                        break;
                    case ChartSeriesType.Bar:
                    case ChartSeriesType.Area:
                    default:
                        throw new NotImplementedException();
                }
                return series;
            }

            public override void WriteJson(JsonWriter writer, ChartSeries value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }
        public class ColorConvertelr : JsonConverter<ColorARGB>
        {
            public override bool CanWrite => true;
            public override bool CanRead => true;
            public override ColorARGB ReadJson(JsonReader reader, Type objectType, [AllowNull] ColorARGB existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var val = reader.Value.ToString();
                return ColorARGB.FromUInt32(UInt32.Parse(val.Substring(1), System.Globalization.NumberStyles.HexNumber));
            }

            public override void WriteJson(JsonWriter writer, [AllowNull] ColorARGB value, JsonSerializer serializer)
            {
                writer.WriteValue(value.ToString("#"));
            }
        }
    }
}
