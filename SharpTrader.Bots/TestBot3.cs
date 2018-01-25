using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    public class TestBot3 : TraderBot
    {
        private bool FirstPass = true;
         
        private IMarketApi MarketApi;
        private ISymbolFeed TradeSymbolFeed;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfEnter;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfExit;
        private BollingerBands BollingerBands;
        private MeanAndVariance MeanAndVariance;
        private TimeSerieNavigator<MeanAndVariance.Record> MeanNavigator;
        private MeanAndVariance MeanAndVarianceShort;
        private TimeSerieNavigator<BollingerBands.Record> BollNavigator;
        private ISymbolFeed RefSymbolFeed;

        private Line LineBollTop = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        private Line LineBollMid = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        private Line LineBollBot = new Line() { Color = new ColorARGB(255, 0, 150, 150) };


        public TimeSpan TimeframeEnter { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan TimeframeExit { get; set; } = TimeSpan.FromMinutes(1);
        public int Boll_Period { get; set; } = 45;
        public double Boll_Dev { get; set; } = 2;
        public ISymbolFeed GraphFeed { get; private set; }
        public List<Indicator> GraphIndicators { get; private set; } = new List<Indicator>();
        public string TradeSymbol = "ETHBTC";
        public string Market = "Binance";


        class Position
        {
            public DateTime Time;
            public int FeaturesId = -1;
            public double EnterPrice;
        }

        List<Position> OpenPositions = new List<Position>();

        public TestBot3(IMarketApi market) : base(null)
        {
            MarketApi = market;
        }

        public override void Start()
        { 
            TfEnter.frame = TimeframeEnter;
            TfExit.frame = TimeframeExit; 

            GraphFeed = TradeSymbolFeed = MarketApi.GetSymbolFeed(TradeSymbol); 

            TfEnter.ticks = TradeSymbolFeed.GetNavigator(TfEnter.frame);
            TfExit.ticks = TradeSymbolFeed.GetNavigator(TfExit.frame);

            BollingerBands = new BollingerBands("Boll5m", Boll_Period, Boll_Dev, TfEnter.ticks);
            BollNavigator = BollingerBands.GetNavigator();

            MeanAndVariance = new MeanAndVariance(130, TfEnter.ticks);
            MeanNavigator = MeanAndVariance.GetNavigator();
            MeanAndVarianceShort = new MeanAndVariance(40, TfEnter.ticks);
            Drawer.Candles = new TimeSerieNavigator<ICandlestick>(TfEnter.ticks);

            Drawer.Lines.AddRange(new[] { LineBollBot, LineBollMid, LineBollTop });
             
            //----------- subscribe to events
            TradeSymbolFeed.OnTick += OnTickHandle;
            TradeSymbolFeed.SubscribeToNewCandle(this, TfEnter.frame);
            TradeSymbolFeed.SubscribeToNewCandle(this, TfExit.frame);
            RefSymbolFeed = MarketApi.GetSymbolFeed("BTCUSDT");
        }

        private void OnTickHandle(ISymbolFeed obj)
        {
            BollingerBands.Calculate();
            MeanAndVariance.Calculate();
            MeanAndVarianceShort.Calculate();

            bool newTfEnter = TfEnter.ticks.Next();

            if (FirstPass)
            {
                FirstPass = false;
                TfExit.ticks.SeekLast();
                TfEnter.ticks.SeekLast();
                BollNavigator.SeekLast();

                TfExit.ticks.Previous();
                TfEnter.ticks.Previous();
                BollNavigator.Previous();
            }


            //if (Status == BotStatus.SearchEnter)
            {

                if (newTfEnter)
                    SearchEnter();
            }
            if (OpenPositions.Count > 0)
            {

                if (TfExit.ticks.Next())
                    SearchExit();
            }
        }

        private double GetAmountToTrade()
        {
            int maxBuys = 40;
            //double portFolioValue = Binance.GetBtcPortfolioValue();
            var price = TfEnter.ticks.Tick.Close;
            double tradable = 0;
            if (OpenPositions.Count >= maxBuys)
                tradable = MarketApi.GetBalance("BTC");
            else
                tradable = (MarketApi.GetBalance("BTC") / (maxBuys - OpenPositions.Count)) / price;
            var (min, step) = MarketApi.GetMinTradable(TradeSymbol);

            if (tradable >= min)
            {
                var nearestMultiple = NearestRound(tradable, step);
                return Math.Max(min, nearestMultiple);
            }
            else
                Console.WriteLine($"Tradable quantity {tradable} is lower than min {min} ");
            return 0;
        }

        public override void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle)
        {

        }

        private double NearestRound(double x, double delX)
        {
            if (delX < 1)
            {
                double i = Math.Floor(x);
                double x2 = i;
                while ((x2 += delX) < x) ;
                double x1 = x2 - delX;
                return (Math.Abs(x - x1) < Math.Abs(x - x2)) ? x1 : x2;
            }
            else
            {
                return (float)Math.Round(x / delX, MidpointRounding.AwayFromZero) * delX;
            }
        }


        private void SearchEnter()
        {
            if (BollingerBands.IsReady)
            {
                var newBollinger = BollNavigator.Next();
                BollNavigator.SeekLast();
                MeanNavigator.SeekLast();

                try
                {
                    ICandlestick candle = TfEnter.ticks.LastTick;
                    var bollTick = BollNavigator.Tick;

                    if (BollNavigator.Count < BollingerBands.Period + 1)
                        return;
                    var bollback = BollNavigator.GetFromCursor(BollingerBands.Period);

                    var tickToAdd = BollNavigator.Tick;

                    if (newBollinger)
                    {
                        LineBollTop.Points.Add(new Point(tickToAdd.Time, tickToAdd.Top));
                        LineBollMid.Points.Add(new Point(tickToAdd.Time, tickToAdd.Main));
                        LineBollBot.Points.Add(new Point(tickToAdd.Time, tickToAdd.Bottom));
                    }

                    if (MeanNavigator.Count < MeanAndVariance.Period + 1)
                        return;
                    var growing = (MeanNavigator.Tick.Mean - MeanNavigator.GetFromCursor(MeanAndVariance.Period).Mean)
                        / MeanNavigator.Tick.Mean > -0.0001;

                    var closeUnderDev = candle.Close < bollTick.Bottom;
                    //var stdvHi = bollTick.Deviation / candle.Close > 0.02;
                    var stdvHi = (bollTick.Main - candle.Close) / candle.Close > 0.04;
                    var askPrice = TradeSymbolFeed.Ask;
                    if (closeUnderDev && stdvHi)
                    {
                        try
                        {
                            var toTrade = GetAmountToTrade();
                            if (toTrade <= 0)
                                return; 

                            Console.WriteLine($"Buying {TradeSymbol} - {toTrade}@{askPrice} - last close {candle.Close}");
                            MarketApi.MarketOrder(TradeSymbolFeed.Symbol, TradeType.Buy, toTrade);
                            OpenPositions.Add(new Position()
                            {
                                Time = candle.Time,
                                EnterPrice = candle.Close, 
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Exception during buy {TradeSymbol}: \n\t {ex.Message}");
                        }
                    }
                }
                catch { }
            }
        }

        private void SearchExit()
        {
            if (BollingerBands.IsReady)
            {
                BollNavigator.SeekLast();
                var bollTick = BollNavigator.Tick;
                var bollTick1 = BollNavigator.PreviousTick;

                var candle = TfExit.ticks.LastTick;
                var bid = this.TradeSymbolFeed.Bid;
                var closeOnMean = bid >= bollTick.Main;
                var closeOnTop = bid >= bollTick.Top;
                var closeOnMidway = bid >= bollTick.Main + bollTick.Deviation / 2;
                if (closeOnMidway)
                {
                    var symbolBalance = MarketApi.GetBalance(TradeSymbolFeed.Asset);
                    MarketApi.MarketOrder(TradeSymbolFeed.Symbol, TradeType.Sell, symbolBalance);
                    Console.WriteLine($"Selling {TradeSymbol} - {symbolBalance}@{bid} - last close {candle.Close}");
                    foreach (var op in OpenPositions)
                    {
                        var tradeLine = new Line();
                        tradeLine.Points.Add(new Point(op.Time, op.EnterPrice));
                        var btcWon = (1d / op.EnterPrice) * (candle.Close - op.EnterPrice);

                        if ((op.EnterPrice * 1.003) > candle.Close)
                        {
                            tradeLine.Color = new ColorARGB(255, 255, 10, 10); 
                        }

                        else
                        {
                            tradeLine.Color = new ColorARGB(255, 10, 10, 255); 
                        }
                        tradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
                        Drawer.Lines.Add(tradeLine);
                    }
                    OpenPositions.Clear();
                }

            }

        }
    }
}
