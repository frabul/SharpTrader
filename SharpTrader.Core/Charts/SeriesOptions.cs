namespace SharpTrader.Charts
{
    public class SeriesOptions
    {
#pragma warning disable IDE1006 // Naming Styles - needed by serialization
        public ColorARGB color { get; set; } = ARGBColors.Black; 
        public int lineWidth { get; set; } = 1;
        public LineStyle style { get; set; } = LineStyle.Solid;
        public Margins margins { get; set; }
        public string priceScaleId { get; set; }
    }
#pragma warning restore IDE1006 // Naming Styles
}
