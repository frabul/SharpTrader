using System;
using System.Collections.Generic;

namespace SharpTrader.Charts
{
#pragma warning disable IDE1006 // Naming Styles
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
#pragma warning restore IDE1006 // Naming Styles
}
