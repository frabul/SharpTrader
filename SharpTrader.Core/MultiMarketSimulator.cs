using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    public partial class MultiMarketSimulator : IMarketApi
    {
        string[] _Markets;
        List<SymbolFeed> Feeds;
        SymbolData[] SymbolsData;

        public DateTime Time { get; private set; }

        public MultiMarketSimulator(string dataDirectory)
        {

        }

        public IEnumerable<string> Markets => _Markets;

        public MarginTrade ClosePosition(Position pos, double price = 0)
        {
            throw new NotImplementedException();
        }

        public ISymbolFeed GetSymbolFeed(string market, string symbol)
        {
            var sf = Feeds.Where(sd => sd.Market == market && sd.Symbol == symbol).FirstOrDefault();
            if (sf == null)
            {
                if (!Markets.Contains(market))
                    throw new InvalidOperationException("Market unavailable");
                var symbolData = SymbolsData.Where(sd => sd.Market == market && sd.Symbol == symbol).FirstOrDefault();
                if (symbolData == null)
                    throw new Exception($"Symbol {symbol} data not found for market {market}");
                //todo create symbol feed, add the data up to current date and 
                sf = new SymbolFeed();

            }
            return sf;
        }


        public IEnumerable<string> GetSymbols(string market)
        {
            throw new NotImplementedException();
        }

        public IMarketOperation LimitOrder(string market, string symbol, TradeType type, double amount, double rate)
        {
            throw new NotImplementedException();
        }

        public IMarketOperation MarketOrder(string market, string symbol, TradeType type, double amount)
        {
            throw new NotImplementedException();
        }

        private class SymbolData
        {
            public Candle[] Data;
            public string Market;
            public string Symbol;
            public double Spread;
        }

        public class MarketInfo
        {
            string Name;
            double Fee;

        }
    }


}
