using ProtoBuf;
using System;

namespace SharpTrader.Storage
{
    [ProtoContract]
    public class SymbolHistoryId
    {
        [ProtoMember(1)]
        public string Symbol { get; set; }
        [ProtoMember(2)]
        public string Market { get; set; }
        [ProtoMember(3)]
        public TimeSpan Resolution { get; set; }

        internal string Key => $"{this.Market}_{this.Symbol}_{(int)this.Resolution.TotalMilliseconds}";
        /// <summary>
        /// Constructor used only by serialization
        /// </summary>
        public SymbolHistoryId()
        {

        }

        public SymbolHistoryId(string market, string symbol, TimeSpan frame)
        {
            this.Market = market;
            this.Symbol = symbol;
            this.Resolution = frame;
        }

        internal static SymbolHistoryId Parse(string key)
        {
            string[] parts = key.Split('_');
            if (parts.Length == 3)
            {
                var market = parts[0];
                var symbol = parts[1];
                var time = TimeSpan.FromMilliseconds(int.Parse(parts[2]));
                return new SymbolHistoryId(market, symbol, time);
            }
            else
            {
                throw new Exception("Invalid SymbolHistoryId string.");
            }
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }
    }

}
