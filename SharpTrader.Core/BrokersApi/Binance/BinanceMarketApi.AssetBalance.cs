namespace SharpTrader.BrokersApi.Binance
{ 
    class AssetBalance
    {
        public string Asset;
        public decimal Free;
        public decimal Locked;
        public decimal Total => Free + Locked;
    }
}
