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

    class Request<T> : TaskCompletionSource<T>
    {
        public RequestType Type { get; }
        public DateTime Time { get; }

        public Request(RequestType type)
        {
            Time = DateTime.Now;
            Type = type;
        }
    }

    public partial class TwsApi : DefaultEWrapper
    {
        static readonly TimeSpan TimoutSmall = TimeSpan.FromMilliseconds(2000);
        private Logger Logger;
        private EReaderMonitorSignal EReaderSignal;
        public bool IsServerConnected = true;
        public string AccountName { get; private set; }
        public bool IsTwsConnected { get; private set; }

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
                //start main task
                _ = Task.Factory.StartNew(MainTask, TaskCreationOptions.LongRunning);
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
            Console.WriteLine("Acct Summary. ReqId: " + reqId + ", Acct: " + account + ", Tag: " + tag + ", Value: " + value + ", Currency: " + currency);
        }

        public override void accountSummaryEnd(int reqId)
        {
            Console.WriteLine("AccountSummaryEnd. Req Id: " + reqId + "\n");
        }

        public override void updateAccountValue(string key, string value, string currency, string accountName)
        {
            Console.WriteLine("UpdateAccountValue. Key: " + key + ", Value: " + value + ", Currency: " + currency + ", AccountName: " + accountName);
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
            Console.WriteLine("Account download finished: " + account + "\n");
        }

        public override void error(int id, int errorCode, string errorMsg)
        {
            if (id == -1 && new[] { 2104, 2106 }.Contains(errorCode))
            {
                IsServerConnected = true;
            }
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

    }
    public enum RequestType
    {
        Connect,

    }


}
