using BinanceExchange.API.Client;
using BinanceExchange.API.Websockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTrader.Tests
{
    public class TestBinanceMarketApi
    {
        public static void Test()
        {
            string apiKey = "1y4h4Pi7MvxG5MKZm1FrbAFXRG3sc8kGwuSDeQpxj3imMJn6XXWKebBzIYxNbcsn";
            string secretKey = "1QNCYC26iNzFwYlKGFfo7iAAkZGmcPkGknH2956jYqPPJk8sOb2RTwaVCDDEqbzb";
            //Initialise the general client client with config
            var client = new BinanceClient(new ClientConfiguration()
            {
                ApiKey = apiKey,
                SecretKey = secretKey,
            });
            var wscli = new DisposableBinanceWebSocketClient(client);
            var UserDataSocket = wscli.ConnectToUserDataWebSocket(new UserDataWebSocketMessages()
            {
                AccountUpdateMessageHandler = d => System.Console.WriteLine("asd"),
                OrderUpdateMessageHandler = d => System.Console.WriteLine("asd"),
                TradeUpdateMessageHandler = d => System.Console.WriteLine("asd"),
            }).Result;


            BinanceMarketApi api2 =
                new BinanceMarketApi(
                    "1y4h4Pi7MvxG5MKZm1FrbAFXRG3sc8kGwuSDeQpxj3imMJn6XXWKebBzIYxNbcsn",
                    "1QNCYC26iNzFwYlKGFfo7iAAkZGmcPkGknH2956jYqPPJk8sOb2RTwaVCDDEqbzb"
                    );
            var bal = api2.GetBalance("ETH");
            var prec = api2.GetSymbolPrecision("ETHBTC");

            var feed = api2.GetSymbolFeed("ADAETH");
            feed.OnTick += Feed_OnTick;
        }

        private static void Feed_OnTick(ISymbolFeed obj)
        {
            Console.WriteLine($"Tick {obj.Ask} - {obj.Bid}");
        }
    }
}
