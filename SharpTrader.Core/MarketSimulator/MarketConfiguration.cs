namespace SharpTrader.MarketSimulator
{
    public class MarketConfiguration
    {
        public string MarketName { get; set; }
        public decimal MakerFee { get; set; }
        public decimal TakerFee { get; set; }
        public bool AllowBorrow { get; set; }
    }

}
