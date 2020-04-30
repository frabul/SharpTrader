#pragma warning disable CS1998

namespace SharpTrader.MarketSimulator
{
    public class AssetBalance
    {
        public decimal Free;
        public decimal Locked;
        public decimal Total => Free + Locked;
        public override string ToString()
        {
            return $"{{Free: {Free}, Locked: {Locked}}}";
        }
    }
}
