using Binance.API.Csharp.Client;
using be = Binance.API.Csharp.Client.Models.Enums;
using Binance.API.Csharp.Client.Models.Account;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.API.Csharp.Client.Models.UserStream;
using Binance.API.Csharp.Client.Domain.Abstract;
using Binance.API.Csharp.Client.Models.WebSocket;

using SymbolsTable = System.Collections.Generic.Dictionary<string, (string Asset, string Quote)>;
using SharpTrader.Core;
using Binance.API.Csharp.Client.Models.Market.TradingRules;

namespace SharpTrader
{
    public class BinanceMarketApi : IMarketApi
    {
        private HistoricalRateDataBase HistoryDb = new HistoricalRateDataBase(".\\Data\\");
        private BinanceClient Client;
        private TradingRules ExchangeInfo;
        private List<NewOrder> NewOrders = new List<NewOrder>();
        private UserStreamInfo UserDataStream;
        private Dictionary<string, double> _Balances = new Dictionary<string, double>();
        private string UserDataListenKey;
        private SymbolsTable SymbolsTable = new SymbolsTable();
        private List<SymbolFeed> Feeds = new List<SymbolFeed>();

        public string MarketName => "Binance";
        public bool Test { get; set; }
        public DateTime Time => throw new NotImplementedException();

        public IEnumerable<ISymbolFeed> ActiveFeeds => throw new NotImplementedException();

        public IEnumerable<ITrade> Trades => throw new NotImplementedException();

        public (string Symbol, double balance)[] Balances => _Balances.Select(kv => (kv.Key, kv.Value)).ToArray();


        public BinanceMarketApi(string secretKey, string apiSecret)
        {
            Client = new BinanceClient(new ApiClient(secretKey, apiSecret));
            //todo, return error
            //Client.TestConnectivity();
            //UserDataStream = Client.StartUserStream().Result;
            //TODO keep alive stream
            ExchangeInfo = Client.GetTradingRulesAsync().Result;
            foreach (var symb in ExchangeInfo.Symbols)
            {
                SymbolsTable.Add(symb.SymbolName, (symb.BaseAsset, symb.QuoteAsset));
            }
            UserDataListenKey = Client.ListenUserDataEndpoint(
                HandleAccountUpdatedMessage,
                HandleOrderUpdateMsg,
                HandleTradeUpdateMsg);

        }

        private void HandleAccountUpdatedMessage(AccountUpdatedMessage msg)
        {
            foreach (var bal in msg.Balances)
            {
                this._Balances[bal.Asset] = (double)bal.Free;
            }
        }

        private void HandleOrderUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {

        }

        private void HandleTradeUpdateMsg(OrderOrTradeUpdatedMessage msg)
        {

        }

        public double GetBalance(string asset)
        {
            if (_Balances.ContainsKey(asset))
                return _Balances[asset];
            return 0;
        }

        public double GetBtcPortfolioValue()
        {
            throw new NotImplementedException();
        }

        public ISymbolFeed GetSymbolFeed(string symbol)
        {
            var feed = Feeds.Where(sf => sf.Symbol == symbol).FirstOrDefault();
            if (feed == null)
            {
                var (Asset, Quote) = SymbolsTable[symbol];
                feed = new SymbolFeed(Client, HistoryDb, MarketName, symbol, Asset, Quote);
                Feeds.Add(feed);
            }
            return feed;
        }

        public IMarketOperation LimitOrder(string symbol, TradeType type, double amount, double rate)
        {
            throw new NotImplementedException();
        }

        public IMarketOperation MarketOrder(string symbol, TradeType type, double amount)
        {
            var side = type == TradeType.Buy ? be.OrderSide.BUY : be.OrderSide.SELL;
            try
            {
                dynamic res;
                if (Test)
                    res = Client.PostNewOrderTest(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                else
                {
                    var ord = Client.PostNewOrder(symbol, (decimal)amount, 0, side, be.OrderType.MARKET, recvWindow: 3000).Result;
                    NewOrders.Add(ord);
                }
                return new MarketOperation(MarketOperationStatus.Completed);
            }
            catch (Exception ex)
            {
                return new MarketOperation(MarketOperationStatus.Failed);
            }
        }

        class MarketOperation : IMarketOperation
        {
            public MarketOperation(MarketOperationStatus status)
            {
                Status = status;
            }
            public MarketOperationStatus Status { get; internal set; }
        }

        class SymbolFeed : SymbolFeedBoilerplate, ISymbolFeed
        {
            private BinanceClient Client;
            private TimeSpan BaseTimeframe = TimeSpan.FromSeconds(60);
            private HistoricalRateDataBase HistoryDb;
            private object Locker = new object();

            public string Symbol { get; private set; }
            public string Asset { get; private set; }
            public string QuoteAsset { get; private set; }
            public double Ask { get; private set; }
            public double Bid { get; private set; }
            public string Market { get; private set; }
            public double Spread { get; set; }
            public double Volume24H { get; private set; }

            public SymbolFeed(BinanceClient client, HistoricalRateDataBase hist, string market, string symbol, string asset, string quoteAsset)
            {
                HistoryDb = hist;
                this.Client = client;
                this.Symbol = symbol;
                this.Market = market;
                this.QuoteAsset = quoteAsset;
                this.Asset = asset;
               

                //load missing data to hist db

                Console.WriteLine($"Downloading history for the requested symbol: {Symbol}");
                var downloader = new SharpTrader.Utils.BinanceDataDownloader(HistoryDb);
                downloader.DownloadCompleteSymbolHistory(Symbol);

                ISymbolHistory symbolHistory = HistoryDb.GetSymbolHistory(this.Market, Symbol, TimeSpan.FromSeconds(60));

                while (symbolHistory.Ticks.Next())
                    this.Ticks.AddRecord(symbolHistory.Ticks.Tick);

                Client.ListenKlineEndpoint(this.Symbol.ToLower(), be.TimeInterval.Minutes_1, HandleKlineEvent);
                Client.ListenPartialDepthEndPoint(this.Symbol.ToLower(), 5, HandleDepthUpdate);

            }

            private void HandleDepthUpdate(DepthPartialMessage messageData)
            {
                this.Bid = (double)messageData.Bids.FirstOrDefault().Price;
                this.Ask = (double)messageData.Asks.FirstOrDefault().Price;
                Spread = Ask - Bid;
                SignalTick();
            }

            private void HandleKlineEvent(KlineMessage msg)
            {
                this.Bid = (double)msg.KlineInfo.Close;

                if (msg.KlineInfo.IsFinal)
                {
                    var ki = msg.KlineInfo;
                    var candle = new Candlestick()
                    {
                        Close = (double)msg.KlineInfo.Close,
                        High = (double)msg.KlineInfo.High,
                        CloseTime = (msg.KlineInfo.EndTime + 1).ToDatetimeMilliseconds(),
                        OpenTime = msg.KlineInfo.StartTime.ToDatetimeMilliseconds(),
                        Low = (double)msg.KlineInfo.Low,
                        Open = (double)msg.KlineInfo.Open,
                        Volume = (double)msg.KlineInfo.Volume
                    };
                    BaseTimeframe = candle.CloseTime - candle.OpenTime;
                    Ticks.AddRecord(candle);
                    foreach (var derived in DerivedTicks)
                    {
                        AddTickToDerivedChart(derived, candle);
                    }
                    SignalTick();
                }
                RaisePendingEvents(this);
            }
        }
    }
}
