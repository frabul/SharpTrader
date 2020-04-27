using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public class PlotHelper
    {
        private object Locker = new object();
        public string Title { get; set; }
        public List<(DateTime Time, double Value)> Points { get; set; } = new List<(DateTime Time, double Value)>();
        public List<Line> Lines { get; set; } = new List<Line>();
        public List<double> HorizontalLines { get; set; } = new List<double>();
        public List<Line> VerticalLines { get; set; } = new List<Line>();
        public TimeSerieNavigator<ITradeBar> Candles { get; set; } = new TimeSerieNavigator<ITradeBar>();
        List<ITrade> TradesToAdd { get; } = new List<ITrade>();
        public (DateTime start, DateTime end) InitialView { get; set; }
        public PlotHelper(string title)
        {
            this.Title = title;

        }
        public void PlotTrade(ITrade newTrade)
        {
            lock (Locker)
            {
                if (newTrade.Direction == TradeDirection.Buy)
                {
                    TradesToAdd.Add(newTrade);
                }
                else
                {
                    var arrival = new Point(newTrade.Time, (double)newTrade.Price);
                    foreach (var tr in TradesToAdd)
                    {
                        var start = new Point(tr.Time, (double)tr.Price);
                        if (tr.Price == 0)
                            Console.WriteLine("price 0");
                        var color = arrival.Y * (1 - 0.002) > start.Y ?
                            new ColorARGB(255, 10, 10, 255) : new ColorARGB(255, 255, 10, 10);
                        this.Lines.Add(new Line(new List<Point>() { start, arrival }, color));
                    }
                    TradesToAdd.Clear();
                }
            }

            //if it is sell trade add lines from the last not signaled trades to this  
        }
        public void PlotLine(IEnumerable<IBaseData> values, ColorARGB color, string axixId = null)
        {
            var points = values.Select(v => new Point(v.Time, v.Value));
            var chartLine = new Line(points, color, axixId) { Color = color };
            Lines.Add(chartLine);
        }
        public void PlotLine<T>(TimeSerieNavigator<T> timeSerie,
                                ColorARGB color,
                                Func<T, double> valuesSelector, string axixId = null) where T : ITimeRecord
        {
            var myNavigator = new TimeSerieNavigator<T>(timeSerie);
            var chartLine = new Line(new List<Point>(), color, axixId);

            Lines.Add(chartLine);


            while (myNavigator.MoveNext())
            {
                var value = valuesSelector(myNavigator.Current);
                chartLine.Points.Add(new Point(myNavigator.Current.Time, value));
            }

            myNavigator.OnNewRecord += (T obj) =>
            {
                while (myNavigator.MoveNext())
                {
                    var value = valuesSelector(myNavigator.Current);
                    lock (chartLine)
                        chartLine.Points.Add(new Point(myNavigator.Current.Time, value));
                }
            };
        }


        public void PlotLines<T>(IEnumerable<T> values,
                                 ColorARGB[] color,
                                 Func<T, double[]> valuesSelector, string axixId = null) where T : ITimeRecord
        {
            var firstTickValues = valuesSelector(values.First());
            List<Point>[] lines = new List<Point>[firstTickValues.Length];

            foreach (var val in values)
            {
                var components = valuesSelector(val);
                for (int i = 0; i < components.Length; i++)
                    lines[i].Add(new Point(val.Time, components[i]));
            }

            for (int i = 0; i < lines.Length; i++)
                Lines.Add(new Line(lines[i], color[i], axixId));
        }

        public void PlotOperation(ITrade buy, ITrade sell, bool isLong = true)
        {
            var p1 = new Point(buy.Time, (double)buy.Price);
            var p2 = new Point(sell.Time, (double)sell.Price);
            ColorARGB color;
            if (isLong)
                color = buy.Price < sell.Price ?
                                new ColorARGB(255, 10, 255, 10) : ARGBColors.Blue;
            else
                color = buy.Price < sell.Price ?
                                ARGBColors.Blue : new ColorARGB(255, 10, 255, 10);
            this.Lines.Add(new Line(new Point[] { p1, p2 }, color));
        }

        public void PlotVerticalLine(DateTime time, ColorARGB color)
        {
            var p1 = new Point(time, (double)-1);
            var p2 = new Point(time, (double)1);
            this.VerticalLines.Add(new Line(new[] { p1, p2 }, color, null));
        }
    }

    public class Line
    {
        public List<Point> Points;
        public ColorARGB Color;
        public string AxisId;

        public Line(IEnumerable<Point> points, ColorARGB color, string axisId = null)
        {
            Points = points.ToList();
            Color = color;
            AxisId = axisId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ColorARGB
    {
        [FieldOffset(0)]
        public byte A;
        [FieldOffset(1)]
        public byte R;
        [FieldOffset(2)]
        public byte G;
        [FieldOffset(3)]
        public byte B;
        [FieldOffset(0)]
        public uint Value;

        public ColorARGB(byte a, byte r, byte g, byte b) : this()
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }
        public static ColorARGB FromUInt32(uint color)
        {
            var a = (byte)(color >> 24);
            var r = (byte)(color >> 16);
            var g = (byte)(color >> 8);
            var b = (byte)(color >> 0);
            return new ColorARGB(a, r, g, b);
        }
    }

    public static class ARGBColors
    {
        /// <summary>
        /// The undefined color.
        /// </summary>
        public static readonly ColorARGB Undefined = ColorARGB.FromUInt32(0x00000000);

        /// <summary>
        /// The automatic color.
        /// </summary>
        public static readonly ColorARGB Automatic = ColorARGB.FromUInt32(0x00000001);

        /// <summary>
        /// The alice blue.
        /// </summary>
        public static readonly ColorARGB AliceBlue = ColorARGB.FromUInt32(0xFFF0F8FF);

        /// <summary>
        /// The antique white.
        /// </summary>
        public static readonly ColorARGB AntiqueWhite = ColorARGB.FromUInt32(0xFFFAEBD7);

        /// <summary>
        /// The aqua.
        /// </summary>
        public static readonly ColorARGB Aqua = ColorARGB.FromUInt32(0xFF00FFFF);

        /// <summary>
        /// The aquamarine.
        /// </summary>
        public static readonly ColorARGB Aquamarine = ColorARGB.FromUInt32(0xFF7FFFD4);

        /// <summary>
        /// The azure.
        /// </summary>
        public static readonly ColorARGB Azure = ColorARGB.FromUInt32(0xFFF0FFFF);

        /// <summary>
        /// The beige.
        /// </summary>
        public static readonly ColorARGB Beige = ColorARGB.FromUInt32(0xFFF5F5DC);

        /// <summary>
        /// The bisque.
        /// </summary>
        public static readonly ColorARGB Bisque = ColorARGB.FromUInt32(0xFFFFE4C4);

        /// <summary>
        /// The black.
        /// </summary>
        public static readonly ColorARGB Black = ColorARGB.FromUInt32(0xFF000000);

        /// <summary>
        /// The blanched almond.
        /// </summary>
        public static readonly ColorARGB BlanchedAlmond = ColorARGB.FromUInt32(0xFFFFEBCD);

        /// <summary>
        /// The blue.
        /// </summary>
        public static readonly ColorARGB Blue = ColorARGB.FromUInt32(0xFF0000FF);

        /// <summary>
        /// The blue violet.
        /// </summary>
        public static readonly ColorARGB BlueViolet = ColorARGB.FromUInt32(0xFF8A2BE2);

        /// <summary>
        /// The brown.
        /// </summary>
        public static readonly ColorARGB Brown = ColorARGB.FromUInt32(0xFFA52A2A);

        /// <summary>
        /// The burly wood.
        /// </summary>
        public static readonly ColorARGB BurlyWood = ColorARGB.FromUInt32(0xFFDEB887);

        /// <summary>
        /// The cadet blue.
        /// </summary>
        public static readonly ColorARGB CadetBlue = ColorARGB.FromUInt32(0xFF5F9EA0);

        /// <summary>
        /// The chartreuse.
        /// </summary>
        public static readonly ColorARGB Chartreuse = ColorARGB.FromUInt32(0xFF7FFF00);

        /// <summary>
        /// The chocolate.
        /// </summary>
        public static readonly ColorARGB Chocolate = ColorARGB.FromUInt32(0xFFD2691E);

        /// <summary>
        /// The coral.
        /// </summary>
        public static readonly ColorARGB Coral = ColorARGB.FromUInt32(0xFFFF7F50);

        /// <summary>
        /// The cornflower blue.
        /// </summary>
        public static readonly ColorARGB CornflowerBlue = ColorARGB.FromUInt32(0xFF6495ED);

        /// <summary>
        /// The cornsilk.
        /// </summary>
        public static readonly ColorARGB Cornsilk = ColorARGB.FromUInt32(0xFFFFF8DC);

        /// <summary>
        /// The crimson.
        /// </summary>
        public static readonly ColorARGB Crimson = ColorARGB.FromUInt32(0xFFDC143C);

        /// <summary>
        /// The cyan.
        /// </summary>
        public static readonly ColorARGB Cyan = ColorARGB.FromUInt32(0xFF00FFFF);

        /// <summary>
        /// The dark blue.
        /// </summary>
        public static readonly ColorARGB DarkBlue = ColorARGB.FromUInt32(0xFF00008B);

        /// <summary>
        /// The dark cyan.
        /// </summary>
        public static readonly ColorARGB DarkCyan = ColorARGB.FromUInt32(0xFF008B8B);

        /// <summary>
        /// The dark goldenrod.
        /// </summary>
        public static readonly ColorARGB DarkGoldenrod = ColorARGB.FromUInt32(0xFFB8860B);

        /// <summary>
        /// The dark gray.
        /// </summary>
        public static readonly ColorARGB DarkGray = ColorARGB.FromUInt32(0xFFA9A9A9);

        /// <summary>
        /// The dark green.
        /// </summary>
        public static readonly ColorARGB DarkGreen = ColorARGB.FromUInt32(0xFF006400);

        /// <summary>
        /// The dark khaki.
        /// </summary>
        public static readonly ColorARGB DarkKhaki = ColorARGB.FromUInt32(0xFFBDB76B);

        /// <summary>
        /// The dark magenta.
        /// </summary>
        public static readonly ColorARGB DarkMagenta = ColorARGB.FromUInt32(0xFF8B008B);

        /// <summary>
        /// The dark olive green.
        /// </summary>
        public static readonly ColorARGB DarkOliveGreen = ColorARGB.FromUInt32(0xFF556B2F);

        /// <summary>
        /// The dark orange.
        /// </summary>
        public static readonly ColorARGB DarkOrange = ColorARGB.FromUInt32(0xFFFF8C00);

        /// <summary>
        /// The dark orchid.
        /// </summary>
        public static readonly ColorARGB DarkOrchid = ColorARGB.FromUInt32(0xFF9932CC);

        /// <summary>
        /// The dark red.
        /// </summary>
        public static readonly ColorARGB DarkRed = ColorARGB.FromUInt32(0xFF8B0000);

        /// <summary>
        /// The dark salmon.
        /// </summary>
        public static readonly ColorARGB DarkSalmon = ColorARGB.FromUInt32(0xFFE9967A);

        /// <summary>
        /// The dark sea green.
        /// </summary>
        public static readonly ColorARGB DarkSeaGreen = ColorARGB.FromUInt32(0xFF8FBC8F);

        /// <summary>
        /// The dark slate blue.
        /// </summary>
        public static readonly ColorARGB DarkSlateBlue = ColorARGB.FromUInt32(0xFF483D8B);

        /// <summary>
        /// The dark slate gray.
        /// </summary>
        public static readonly ColorARGB DarkSlateGray = ColorARGB.FromUInt32(0xFF2F4F4F);

        /// <summary>
        /// The dark turquoise.
        /// </summary>
        public static readonly ColorARGB DarkTurquoise = ColorARGB.FromUInt32(0xFF00CED1);

        /// <summary>
        /// The dark violet.
        /// </summary>
        public static readonly ColorARGB DarkViolet = ColorARGB.FromUInt32(0xFF9400D3);

        /// <summary>
        /// The deep pink.
        /// </summary>
        public static readonly ColorARGB DeepPink = ColorARGB.FromUInt32(0xFFFF1493);

        /// <summary>
        /// The deep sky blue.
        /// </summary>
        public static readonly ColorARGB DeepSkyBlue = ColorARGB.FromUInt32(0xFF00BFFF);

        /// <summary>
        /// The dim gray.
        /// </summary>
        public static readonly ColorARGB DimGray = ColorARGB.FromUInt32(0xFF696969);

        /// <summary>
        /// The dodger blue.
        /// </summary>
        public static readonly ColorARGB DodgerBlue = ColorARGB.FromUInt32(0xFF1E90FF);

        /// <summary>
        /// The firebrick.
        /// </summary>
        public static readonly ColorARGB Firebrick = ColorARGB.FromUInt32(0xFFB22222);

        /// <summary>
        /// The floral white.
        /// </summary>
        public static readonly ColorARGB FloralWhite = ColorARGB.FromUInt32(0xFFFFFAF0);

        /// <summary>
        /// The forest green.
        /// </summary>
        public static readonly ColorARGB ForestGreen = ColorARGB.FromUInt32(0xFF228B22);

        /// <summary>
        /// The fuchsia.
        /// </summary>
        public static readonly ColorARGB Fuchsia = ColorARGB.FromUInt32(0xFFFF00FF);

        /// <summary>
        /// The gainsboro.
        /// </summary>
        public static readonly ColorARGB Gainsboro = ColorARGB.FromUInt32(0xFFDCDCDC);

        /// <summary>
        /// The ghost white.
        /// </summary>
        public static readonly ColorARGB GhostWhite = ColorARGB.FromUInt32(0xFFF8F8FF);

        /// <summary>
        /// The gold.
        /// </summary>
        public static readonly ColorARGB Gold = ColorARGB.FromUInt32(0xFFFFD700);

        /// <summary>
        /// The goldenrod.
        /// </summary>
        public static readonly ColorARGB Goldenrod = ColorARGB.FromUInt32(0xFFDAA520);

        /// <summary>
        /// The gray.
        /// </summary>
        public static readonly ColorARGB Gray = ColorARGB.FromUInt32(0xFF808080);

        /// <summary>
        /// The green.
        /// </summary>
        public static readonly ColorARGB Green = ColorARGB.FromUInt32(0xFF008000);

        /// <summary>
        /// The green yellow.
        /// </summary>
        public static readonly ColorARGB GreenYellow = ColorARGB.FromUInt32(0xFFADFF2F);

        /// <summary>
        /// The honeydew.
        /// </summary>
        public static readonly ColorARGB Honeydew = ColorARGB.FromUInt32(0xFFF0FFF0);

        /// <summary>
        /// The hot pink.
        /// </summary>
        public static readonly ColorARGB HotPink = ColorARGB.FromUInt32(0xFFFF69B4);

        /// <summary>
        /// The indian red.
        /// </summary>
        public static readonly ColorARGB IndianRed = ColorARGB.FromUInt32(0xFFCD5C5C);

        /// <summary>
        /// The indigo.
        /// </summary>
        public static readonly ColorARGB Indigo = ColorARGB.FromUInt32(0xFF4B0082);

        /// <summary>
        /// The ivory.
        /// </summary>
        public static readonly ColorARGB Ivory = ColorARGB.FromUInt32(0xFFFFFFF0);

        /// <summary>
        /// The khaki.
        /// </summary>
        public static readonly ColorARGB Khaki = ColorARGB.FromUInt32(0xFFF0E68C);

        /// <summary>
        /// The lavender.
        /// </summary>
        public static readonly ColorARGB Lavender = ColorARGB.FromUInt32(0xFFE6E6FA);

        /// <summary>
        /// The lavender blush.
        /// </summary>
        public static readonly ColorARGB LavenderBlush = ColorARGB.FromUInt32(0xFFFFF0F5);

        /// <summary>
        /// The lawn green.
        /// </summary>
        public static readonly ColorARGB LawnGreen = ColorARGB.FromUInt32(0xFF7CFC00);

        /// <summary>
        /// The lemon chiffon.
        /// </summary>
        public static readonly ColorARGB LemonChiffon = ColorARGB.FromUInt32(0xFFFFFACD);

        /// <summary>
        /// The light blue.
        /// </summary>
        public static readonly ColorARGB LightBlue = ColorARGB.FromUInt32(0xFFADD8E6);

        /// <summary>
        /// The light coral.
        /// </summary>
        public static readonly ColorARGB LightCoral = ColorARGB.FromUInt32(0xFFF08080);

        /// <summary>
        /// The light cyan.
        /// </summary>
        public static readonly ColorARGB LightCyan = ColorARGB.FromUInt32(0xFFE0FFFF);

        /// <summary>
        /// The light goldenrod yellow.
        /// </summary>
        public static readonly ColorARGB LightGoldenrodYellow = ColorARGB.FromUInt32(0xFFFAFAD2);

        /// <summary>
        /// The light gray.
        /// </summary>
        public static readonly ColorARGB LightGray = ColorARGB.FromUInt32(0xFFD3D3D3);

        /// <summary>
        /// The light green.
        /// </summary>
        public static readonly ColorARGB LightGreen = ColorARGB.FromUInt32(0xFF90EE90);

        /// <summary>
        /// The light pink.
        /// </summary>
        public static readonly ColorARGB LightPink = ColorARGB.FromUInt32(0xFFFFB6C1);

        /// <summary>
        /// The light salmon.
        /// </summary>
        public static readonly ColorARGB LightSalmon = ColorARGB.FromUInt32(0xFFFFA07A);

        /// <summary>
        /// The light sea green.
        /// </summary>
        public static readonly ColorARGB LightSeaGreen = ColorARGB.FromUInt32(0xFF20B2AA);

        /// <summary>
        /// The light sky blue.
        /// </summary>
        public static readonly ColorARGB LightSkyBlue = ColorARGB.FromUInt32(0xFF87CEFA);

        /// <summary>
        /// The light slate gray.
        /// </summary>
        public static readonly ColorARGB LightSlateGray = ColorARGB.FromUInt32(0xFF778899);

        /// <summary>
        /// The light steel blue.
        /// </summary>
        public static readonly ColorARGB LightSteelBlue = ColorARGB.FromUInt32(0xFFB0C4DE);

        /// <summary>
        /// The light yellow.
        /// </summary>
        public static readonly ColorARGB LightYellow = ColorARGB.FromUInt32(0xFFFFFFE0);

        /// <summary>
        /// The lime.
        /// </summary>
        public static readonly ColorARGB Lime = ColorARGB.FromUInt32(0xFF00FF00);

        /// <summary>
        /// The lime green.
        /// </summary>
        public static readonly ColorARGB LimeGreen = ColorARGB.FromUInt32(0xFF32CD32);

        /// <summary>
        /// The linen.
        /// </summary>
        public static readonly ColorARGB Linen = ColorARGB.FromUInt32(0xFFFAF0E6);

        /// <summary>
        /// The magenta.
        /// </summary>
        public static readonly ColorARGB Magenta = ColorARGB.FromUInt32(0xFFFF00FF);

        /// <summary>
        /// The maroon.
        /// </summary>
        public static readonly ColorARGB Maroon = ColorARGB.FromUInt32(0xFF800000);

        /// <summary>
        /// The medium aquamarine.
        /// </summary>
        public static readonly ColorARGB MediumAquamarine = ColorARGB.FromUInt32(0xFF66CDAA);

        /// <summary>
        /// The medium blue.
        /// </summary>
        public static readonly ColorARGB MediumBlue = ColorARGB.FromUInt32(0xFF0000CD);

        /// <summary>
        /// The medium orchid.
        /// </summary>
        public static readonly ColorARGB MediumOrchid = ColorARGB.FromUInt32(0xFFBA55D3);

        /// <summary>
        /// The medium purple.
        /// </summary>
        public static readonly ColorARGB MediumPurple = ColorARGB.FromUInt32(0xFF9370DB);

        /// <summary>
        /// The medium sea green.
        /// </summary>
        public static readonly ColorARGB MediumSeaGreen = ColorARGB.FromUInt32(0xFF3CB371);

        /// <summary>
        /// The medium slate blue.
        /// </summary>
        public static readonly ColorARGB MediumSlateBlue = ColorARGB.FromUInt32(0xFF7B68EE);

        /// <summary>
        /// The medium spring green.
        /// </summary>
        public static readonly ColorARGB MediumSpringGreen = ColorARGB.FromUInt32(0xFF00FA9A);

        /// <summary>
        /// The medium turquoise.
        /// </summary>
        public static readonly ColorARGB MediumTurquoise = ColorARGB.FromUInt32(0xFF48D1CC);

        /// <summary>
        /// The medium violet red.
        /// </summary>
        public static readonly ColorARGB MediumVioletRed = ColorARGB.FromUInt32(0xFFC71585);

        /// <summary>
        /// The midnight blue.
        /// </summary>
        public static readonly ColorARGB MidnightBlue = ColorARGB.FromUInt32(0xFF191970);

        /// <summary>
        /// The mint cream.
        /// </summary>
        public static readonly ColorARGB MintCream = ColorARGB.FromUInt32(0xFFF5FFFA);

        /// <summary>
        /// The misty rose.
        /// </summary>
        public static readonly ColorARGB MistyRose = ColorARGB.FromUInt32(0xFFFFE4E1);

        /// <summary>
        /// The moccasin.
        /// </summary>
        public static readonly ColorARGB Moccasin = ColorARGB.FromUInt32(0xFFFFE4B5);

        /// <summary>
        /// The navajo white.
        /// </summary>
        public static readonly ColorARGB NavajoWhite = ColorARGB.FromUInt32(0xFFFFDEAD);

        /// <summary>
        /// The navy.
        /// </summary>
        public static readonly ColorARGB Navy = ColorARGB.FromUInt32(0xFF000080);

        /// <summary>
        /// The old lace.
        /// </summary>
        public static readonly ColorARGB OldLace = ColorARGB.FromUInt32(0xFFFDF5E6);

        /// <summary>
        /// The olive.
        /// </summary>
        public static readonly ColorARGB Olive = ColorARGB.FromUInt32(0xFF808000);

        /// <summary>
        /// The olive drab.
        /// </summary>
        public static readonly ColorARGB OliveDrab = ColorARGB.FromUInt32(0xFF6B8E23);

        /// <summary>
        /// The orange.
        /// </summary>
        public static readonly ColorARGB Orange = ColorARGB.FromUInt32(0xFFFFA500);

        /// <summary>
        /// The orange red.
        /// </summary>
        public static readonly ColorARGB OrangeRed = ColorARGB.FromUInt32(0xFFFF4500);

        /// <summary>
        /// The orchid.
        /// </summary>
        public static readonly ColorARGB Orchid = ColorARGB.FromUInt32(0xFFDA70D6);

        /// <summary>
        /// The pale goldenrod.
        /// </summary>
        public static readonly ColorARGB PaleGoldenrod = ColorARGB.FromUInt32(0xFFEEE8AA);

        /// <summary>
        /// The pale green.
        /// </summary>
        public static readonly ColorARGB PaleGreen = ColorARGB.FromUInt32(0xFF98FB98);

        /// <summary>
        /// The pale turquoise.
        /// </summary>
        public static readonly ColorARGB PaleTurquoise = ColorARGB.FromUInt32(0xFFAFEEEE);

        /// <summary>
        /// The pale violet red.
        /// </summary>
        public static readonly ColorARGB PaleVioletRed = ColorARGB.FromUInt32(0xFFDB7093);

        /// <summary>
        /// The papaya whip.
        /// </summary>
        public static readonly ColorARGB PapayaWhip = ColorARGB.FromUInt32(0xFFFFEFD5);

        /// <summary>
        /// The peach puff.
        /// </summary>
        public static readonly ColorARGB PeachPuff = ColorARGB.FromUInt32(0xFFFFDAB9);

        /// <summary>
        /// The peru.
        /// </summary>
        public static readonly ColorARGB Peru = ColorARGB.FromUInt32(0xFFCD853F);

        /// <summary>
        /// The pink.
        /// </summary>
        public static readonly ColorARGB Pink = ColorARGB.FromUInt32(0xFFFFC0CB);

        /// <summary>
        /// The plum.
        /// </summary>
        public static readonly ColorARGB Plum = ColorARGB.FromUInt32(0xFFDDA0DD);

        /// <summary>
        /// The powder blue.
        /// </summary>
        public static readonly ColorARGB PowderBlue = ColorARGB.FromUInt32(0xFFB0E0E6);

        /// <summary>
        /// The purple.
        /// </summary>
        public static readonly ColorARGB Purple = ColorARGB.FromUInt32(0xFF800080);

        /// <summary>
        /// The red.
        /// </summary>
        public static readonly ColorARGB Red = ColorARGB.FromUInt32(0xFFFF0000);

        /// <summary>
        /// The rosy brown.
        /// </summary>
        public static readonly ColorARGB RosyBrown = ColorARGB.FromUInt32(0xFFBC8F8F);

        /// <summary>
        /// The royal blue.
        /// </summary>
        public static readonly ColorARGB RoyalBlue = ColorARGB.FromUInt32(0xFF4169E1);

        /// <summary>
        /// The saddle brown.
        /// </summary>
        public static readonly ColorARGB SaddleBrown = ColorARGB.FromUInt32(0xFF8B4513);

        /// <summary>
        /// The salmon.
        /// </summary>
        public static readonly ColorARGB Salmon = ColorARGB.FromUInt32(0xFFFA8072);

        /// <summary>
        /// The sandy brown.
        /// </summary>
        public static readonly ColorARGB SandyBrown = ColorARGB.FromUInt32(0xFFF4A460);

        /// <summary>
        /// The sea green.
        /// </summary>
        public static readonly ColorARGB SeaGreen = ColorARGB.FromUInt32(0xFF2E8B57);

        /// <summary>
        /// The sea shell.
        /// </summary>
        public static readonly ColorARGB SeaShell = ColorARGB.FromUInt32(0xFFFFF5EE);

        /// <summary>
        /// The sienna.
        /// </summary>
        public static readonly ColorARGB Sienna = ColorARGB.FromUInt32(0xFFA0522D);

        /// <summary>
        /// The silver.
        /// </summary>
        public static readonly ColorARGB Silver = ColorARGB.FromUInt32(0xFFC0C0C0);

        /// <summary>
        /// The sky blue.
        /// </summary>
        public static readonly ColorARGB SkyBlue = ColorARGB.FromUInt32(0xFF87CEEB);

        /// <summary>
        /// The slate blue.
        /// </summary>
        public static readonly ColorARGB SlateBlue = ColorARGB.FromUInt32(0xFF6A5ACD);

        /// <summary>
        /// The slate gray.
        /// </summary>
        public static readonly ColorARGB SlateGray = ColorARGB.FromUInt32(0xFF708090);

        /// <summary>
        /// The snow.
        /// </summary>
        public static readonly ColorARGB Snow = ColorARGB.FromUInt32(0xFFFFFAFA);

        /// <summary>
        /// The spring green.
        /// </summary>
        public static readonly ColorARGB SpringGreen = ColorARGB.FromUInt32(0xFF00FF7F);

        /// <summary>
        /// The steel blue.
        /// </summary>
        public static readonly ColorARGB SteelBlue = ColorARGB.FromUInt32(0xFF4682B4);

        /// <summary>
        /// The tan.
        /// </summary>
        public static readonly ColorARGB Tan = ColorARGB.FromUInt32(0xFFD2B48C);

        /// <summary>
        /// The teal.
        /// </summary>
        public static readonly ColorARGB Teal = ColorARGB.FromUInt32(0xFF008080);

        /// <summary>
        /// The thistle.
        /// </summary>
        public static readonly ColorARGB Thistle = ColorARGB.FromUInt32(0xFFD8BFD8);

        /// <summary>
        /// The tomato.
        /// </summary>
        public static readonly ColorARGB Tomato = ColorARGB.FromUInt32(0xFFFF6347);

        /// <summary>
        /// The transparent.
        /// </summary>
        public static readonly ColorARGB Transparent = ColorARGB.FromUInt32(0x00FFFFFF);

        /// <summary>
        /// The turquoise.
        /// </summary>
        public static readonly ColorARGB Turquoise = ColorARGB.FromUInt32(0xFF40E0D0);

        /// <summary>
        /// The violet.
        /// </summary>
        public static readonly ColorARGB Violet = ColorARGB.FromUInt32(0xFFEE82EE);

        /// <summary>
        /// The wheat.
        /// </summary>
        public static readonly ColorARGB Wheat = ColorARGB.FromUInt32(0xFFF5DEB3);

        /// <summary>
        /// The white.
        /// </summary>
        public static readonly ColorARGB White = ColorARGB.FromUInt32(0xFFFFFFFF);

        /// <summary>
        /// The white smoke.
        /// </summary>
        public static readonly ColorARGB WhiteSmoke = ColorARGB.FromUInt32(0xFFF5F5F5);

        /// <summary>
        /// The yellow.
        /// </summary>
        public static readonly ColorARGB Yellow = ColorARGB.FromUInt32(0xFFFFFF00);

        /// <summary>
        /// The yellow green.
        /// </summary>
        public static readonly ColorARGB YellowGreen = ColorARGB.FromUInt32(0xFF9ACD32);
    }
    public struct Point
    {
        public Point(DateTime x, double y) { X = x; Y = y; }
        public DateTime X { get; }
        public double Y { get; }
    }
}
