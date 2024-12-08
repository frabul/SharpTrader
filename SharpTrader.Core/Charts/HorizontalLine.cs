namespace SharpTrader.Charts
{
    public class HorizontalLine
    {
        public decimal Price { get; set; }
        public ColorARGB Color { get; set; }
        public int LineWidth { get; set; } = 1;
        public LineStyle Style { get; set; } = LineStyle.Solid;
    }
}
