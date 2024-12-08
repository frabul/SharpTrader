using IBApi;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpTrader;

namespace SharpTrader.BrokersApi.InteractiveBrokers
{
    public class TwsClient
    {

    }



    public partial class InteractiveBrokersApi : DefaultEWrapper
    {
        static readonly TimeSpan TimoutSmall = TimeSpan.FromMilliseconds(2000);
        private Logger Logger;
        private EReaderMonitorSignal EReaderSignal;
        public bool IsServerConnected = true;
        public string AccountName { get; private set; }
        public bool IsTwsConnected { get; private set; }

        private List<SymbolFeed> Feeds = new List<SymbolFeed>();

        private async Task MainTask()
        {
            Client.reqCompletedOrders(false);
            while (Client.IsConnected())
            {
                await Task.Delay(1000);
                try
                {
                    Client.reqCurrentTime();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in TwsApi MainTask: {ex.Message}");
                }

            }
        }

        //------------- connect request -------------
        private TaskCompletionSource<bool> ConnectRequest;
        public async Task Connect()
        {
            if (ConnectRequest == null || ConnectRequest.Task.IsCompleted)
            {
                Logger.Info("Requesting connect to Interactive Brokers TWS Api");
                ConnectRequest = new TaskCompletionSource<bool>();
                var ct = new CancellationTokenSource(TimoutSmall);
                ct.Token.Register(() => ConnectRequest.TrySetResult(false), useSynchronizationContext: false);
                Client.eConnect("127.0.0.1", 7497, 0);
            }

            var ok = await ConnectRequest.Task;
            if (ok)
            {
                Client.reqMarketDataType(3);
                //start main task
                _ = Task.Run(MainTask);
            }
            else
            {
                throw new Exception("Unable to connect.");
            }
        }
        public override void connectAck()
        {
            //tws is connected but we should wait for nextValidId too
            Logger.Info("Received connect acknowledge from Interactive Brokers TWS Api");
            this.IsTwsConnected = true;
            //Create a reader to consume messages from the TWS. The EReader will consume the incoming messages and put them in a queue
            var reader = new EReader(Client, EReaderSignal);
            reader.Start();
            //Once the messages are in the queue, an additional thread can be created to fetch them
            new Thread(() => { while (Client.IsConnected()) { EReaderSignal.waitForSignal(); reader.processMsgs(); } }) { IsBackground = true }.Start();
        }

        public override void nextValidId(int orderId)
        {
            Logger.Info($"nextValidId({orderId})");
            //we can now consider connection request successful
            if (this.IsTwsConnected && ConnectRequest?.Task.IsCompleted == false)
                ConnectRequest.TrySetResult(true);
        }

        //--------------------------------------------- 
        public override void managedAccounts(string accountsList)
        {
            var accounts = accountsList.Split(',');
            this.AccountName = accounts[0];
            Logger.Info($"Account name found {AccountName}, enabling updates");
            Client.reqAccountUpdates(true, AccountName);
        }

        public void RequestAccountSummary()
        {
            Logger.Info("Requesting account summary");
            //Client.reqAccountSummary(9001, "All", AccountSummaryTags.GetAllTags());
        }

        public override void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            //Console.WriteLine("Acct Summary. ReqId: " + reqId + ", Acct: " + account + ", Tag: " + tag + ", Value: " + value + ", Currency: " + currency);
        }

        public override void accountSummaryEnd(int reqId)
        {
            //Console.WriteLine("AccountSummaryEnd. Req Id: " + reqId + "\n");
        }

        public override void updateAccountValue(string key, string value, string currency, string accountName)
        {
            //Console.WriteLine("UpdateAccountValue. Key: " + key + ", Value: " + value + ", Currency: " + currency + ", AccountName: " + accountName);
        }

        public override void updatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            Console.WriteLine("UpdatePortfolio. " + contract.Symbol + ", " + contract.SecType + " @ " + contract.Exchange
                + ": Position: " + position + ", MarketPrice: " + marketPrice + ", MarketValue: " + marketValue + ", AverageCost: " + averageCost
                + ", UnrealizedPNL: " + unrealizedPNL + ", RealizedPNL: " + realizedPNL + ", AccountName: " + accountName);
        }

        public override void updateAccountTime(string timestamp)
        {
            Console.WriteLine("UpdateAccountTime. Time: " + timestamp + "\n");
        }

        public override void accountDownloadEnd(string account)
        {
            //Console.WriteLine("Account download finished: " + account + "\n");
        }

        public override void error(int id, int errorCode, string errorMsg)
        {
            Logger.Error("TwsApi error: {0}, {1}, {2}", id, errorCode, errorMsg);
            if (id == -1 && new[] { 2104, 2106 }.Contains(errorCode))
            {
                IsServerConnected = true;
            }
        }

        public override void error(Exception e)
        {
            Logger.Error("TwsApi error: {0}", e.Message);
        }
        public override void error(string str)
        {
            Logger.Error("TwsApi error: {0}", str);

        }
        public override void currentTime(long time)
        {
            this.Time = time.ToDatetime();
        }

        public override void completedOrder(Contract contract, Order order, OrderState orderState)
        {
            Logger.Info($"Completed order info {contract.Symbol}, order {order.OrderId}, orderState {orderState.Status}");
        }

        public override void completedOrdersEnd()
        {
            Logger.Info($"Completed orders info completed.");
        }

        public override void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
        {
            Console.WriteLine("ScannerData. " + reqId + " - Rank: " + rank + ", Symbol: " + contractDetails.Contract.Symbol + ", SecType: " + contractDetails.Contract.SecType + ", Currency: " + contractDetails.Contract.Currency
                + ", Distance: " + distance + ", Benchmark: " + benchmark + ", Projection: " + projection + ", Legs String: " + legsStr);
        }
        public override void scannerParameters(string xml)
        {
            Console.WriteLine("ScannerParameters. " + xml + "\n");
        }

        public override void commissionReport(CommissionReport commissionReport)
        {
            base.commissionReport(commissionReport);
        }
        public override void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            base.orderStatus(orderId, status, filled, remaining, avgFillPrice, permId, parentId, lastFillPrice, clientId, whyHeld, mktCapPrice);
        }


        public void TestMe()
        {

        }

        //----------------------- market data ---------------------------------- 
        private Dictionary<int, MarketDataSubscription> MarketDataSubs = new Dictionary<int, MarketDataSubscription>();
        private List<SymbolInfo> SymbolInfo = new List<SymbolInfo>();
        private SymbolFeed GetSymbolFeed(ISymbolInfo _symbol)
        {
            var symbol = GetSymbol(_symbol);
            var sub = GetDataSub(symbol);
            var feed = new SymbolFeed(symbol, sub);
            return feed;
        }

        private SymbolInfo GetSymbol(ISymbolInfo symbol)
        {
            throw new NotImplementedException();
        }

        private MarketDataSubscription GetDataSub(SymbolInfo symbol)
        {
            MarketDataSubscription sub = MarketDataSubs.Values.FirstOrDefault(ds => ds.Symbol.Key == symbol.Key);
            if (sub == null)
            {
                var nextId = MarketDataSubs.Values.Max(ds => ds.Id);
                var id = nextId++;
                Client.reqMktData(id, symbol, "", false, false, null);
                sub = new MarketDataSubscription()
                {
                    Id = id,
                    Symbol = symbol
                };
            }
            return sub;
        }

        public override void realtimeBar(int reqId, long time, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            Logger.Info("RealTimeBars. " + reqId + " - Time: " + time + ", Open: " + open + ", High: " + high + ", Low: " + low + ", Close: " + close + ", Volume: " + volume + ", Count: " + count + ", WAP: " + WAP);
            if (!MarketDataSubs.TryGetValue(reqId, out MarketDataSubscription sub))
                Logger.Error($"Missing market data subscription id {reqId}");
            else
            {
                var candle = new Candlestick()
                {
                    CloseTime = time.ToDatetime(),
                    Open = open,
                    High = high,
                    Close = close,
                    Low = low,
                    OpenTime = time.ToDatetime().AddSeconds(-5),
                    QuoteAssetVolume = volume
                };
                sub.SignalNewData(candle);
            }
        }
        public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            Logger.Info("Tick Price. Ticker Id:" + tickerId + ", Field: " + field + ", Price: " + price + ", CanAutoExecute: " + attribs.CanAutoExecute +
                ", PastLimit: " + attribs.PastLimit + ", PreOpen: " + attribs.PreOpen);
        }
        public override void tickSize(int tickerId, int field, int size)
        {
            Logger.Info("Tick Size. Ticker Id:" + tickerId + ", Field: " + field + ", Size: " + size);
        }
        public override void tickString(int tickerId, int tickType, string value)
        {
            Logger.Info("Tick string. Ticker Id:" + tickerId + ", Type: " + tickType + ", Value: " + value);
        }
        public override void tickGeneric(int tickerId, int field, double value)
        {
            Logger.Info("Tick Generic. Ticker Id:" + tickerId + ", Field: " + field + ", Value: " + value);
        }
    }

    class MarketDataSubscription
    {
        public event Action<Candlestick> OnUpdate;
        public SymbolInfo Symbol;
        public int Id;
        public void SignalNewData(Candlestick candlestick)
        {
            OnUpdate?.Invoke(candlestick);
        }
    }


    class SymbolInfo : Contract, ISymbolInfo
    {
        string key;
        public string Asset => base.Symbol;
        public bool IsBorrowAllowed { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsMarginTadingAllowed { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsSpotTadingAllowed => true;
        public string Key { get { if (key == null) key = this.Symbol + base.Currency + base.SecType + base.Exchange + base.PrimaryExch; return key; } }
        public decimal LotSizeStep => 1;
        public decimal MinLotSize => 1;
        public decimal MinNotional => 1;
        public decimal PricePrecision => 0.01m;
        public string QuoteAsset => base.Currency;

        public bool IsCrossMarginAllowed => throw new NotImplementedException();

        public bool IsIsolatedMarginAllowed => throw new NotImplementedException();

        public bool IsTradingEnabled => throw new NotImplementedException();
    }


}
