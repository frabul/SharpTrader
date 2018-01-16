using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    public class TestBot : TraderBot
    {
        public TimeSpan TimeframeEnter { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan TimeframeExit { get; set; } = TimeSpan.FromMinutes(1);
        public int Boll_Period { get; set; } = 45;
        public double Boll_Dev { get; set; } = 2;

        double EnterPrice = 0;

        IMarketApi Binance;
        ISymbolFeed OMGBTC;
        (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfEnter;
        (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfExit;
        BollingerBands BollingerBands;
        MeanAndVariance MeanAndVariance;
        private TimeSerieNavigator<MeanAndVariance.Record> MeanNavigator;
        BotStatus Status = BotStatus.SearchEnter;


        public ISymbolFeed GraphFeed { get; private set; }
        public List<Indicator> GraphIndicators { get; private set; } = new List<Indicator>();

        TimeSerieNavigator<BollingerBands.Record> BollNavigator;


        public TestBot(IMarketsManager markets) : base(markets)
        {

        }

        public override void Start()
        {
            TfEnter.frame = TimeframeEnter;
            TfExit.frame = TimeframeExit;

            Binance = MarketsManager.GetMarketApi("Binance");
            GraphFeed = OMGBTC = Binance.GetSymbolFeed("SNGLSBTC");
            OMGBTC.OnTick += OMGBTC_OnTick;
            OMGBTC.SubscribeToNewCandle(this, TfEnter.frame);
            OMGBTC.SubscribeToNewCandle(this, TfExit.frame);

            TfEnter.ticks = OMGBTC.GetNavigator(TfEnter.frame);
            TfExit.ticks = OMGBTC.GetNavigator(TfExit.frame);
            BollingerBands = new BollingerBands("Boll5m", Boll_Period, Boll_Dev, TfEnter.ticks);
            BollNavigator = BollingerBands.GetNavigator();

            MeanAndVariance = new MeanAndVariance(130, TfEnter.ticks);
            MeanNavigator = MeanAndVariance.GetNavigator();

            Drawer.Candles = new TimeSerieNavigator<ICandlestick>(TfEnter.ticks);

            Drawer.Lines.AddRange(new[] { LineBollBot, LineBollMid, LineBollTop });
        }

        bool FirstPass = true;

        private void OMGBTC_OnTick(ISymbolFeed obj)
        {
            BollingerBands.Calculate();
            MeanAndVariance.Calculate();

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


            if (Status == BotStatus.SearchEnter)
            {

                if (newTfEnter)
                    SearchEnter();
            }
            else if (Status == BotStatus.SearchExit)
            {

                if (TfExit.ticks.Next())
                    SearchExit();
            }
        }

        private double GetAmountToTrade()
        {
            var price = TfEnter.ticks.Tick.Close;
            return 1 / price;
        }

        public override void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle)
        {

        }

        Line TradeLine;
        Line LineBollTop = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        Line LineBollMid = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        Line LineBollBot = new Line() { Color = new ColorARGB(255, 0, 150, 150) };

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
                    var bollback = BollNavigator.GetFromCursor(BollingerBands.Period);
               
                    var tickToAdd = BollNavigator.Tick;
                     
                    if (newBollinger)
                    {
                        LineBollTop.Points.Add(new Point(tickToAdd.Time, tickToAdd.Top));
                        LineBollMid.Points.Add(new Point(tickToAdd.Time, tickToAdd.Main));
                        LineBollBot.Points.Add(new Point(tickToAdd.Time, tickToAdd.Bottom));
                    }


                    var growing = (MeanNavigator.Tick.Mean - MeanNavigator.GetFromCursor(MeanAndVariance.Period).Mean)
                        / MeanNavigator.Tick.Mean > -0.0001;
                    var closeUnderDev = candle.Close < bollTick.Bottom;
                    //var stdvHi = bollTick.Deviation / candle.Close > 0.02;
                    var stdvHi = (bollTick.Main - candle.Close) / candle.Close > 0.035;
                    if (closeUnderDev && stdvHi && growing)
                    {
                        Binance.MarketOrder(OMGBTC.Symbol, TradeType.Buy, GetAmountToTrade());
                        Status = BotStatus.SearchExit;
                        EnterPrice = candle.Close;
                        TradeLine = new Line();
                        TradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
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

                var closeOnMean = candle.Close >= bollTick.Main;
                var closeOnTop = candle.Close >= bollTick.Top;
                var meanLowerThanEnter = bollTick.Main < this.EnterPrice;
                var topLowerThanEnter = bollTick.Top < this.EnterPrice;
                var closeOnMidway = candle.Close >= bollTick.Main + bollTick.Deviation / 2;
                if (closeOnMidway || topLowerThanEnter)
                {

                    Binance.MarketOrder(OMGBTC.Symbol, TradeType.Sell, Binance.GetBalance(OMGBTC.Asset));
                    Status = BotStatus.SearchEnter;
                    TradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
                    TradeLine.Color = this.EnterPrice > candle.Close ?
                        new ColorARGB(255, 255, 10, 10) : new ColorARGB(255, 10, 10, 255);
                    Drawer.Lines.Add(TradeLine);
                }

            }

        }


        enum BotStatus
        {
            SearchEnter,
            SearchExit,

        }
    }
}
