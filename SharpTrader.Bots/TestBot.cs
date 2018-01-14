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


        TimeSerieNavigator<BollingerBands.Record> BollNavigator;


        public TestBot(IMarketsManager markets) : base(markets)
        {

        }

        public override void Start()
        {
            TfEnter.frame = TimeframeEnter;
            TfExit.frame = TimeframeExit;

            Binance = MarketsManager.GetMarketApi("Binance");
            OMGBTC = Binance.GetSymbolFeed("TNBBTC");
            OMGBTC.OnTick += OMGBTC_OnTick;
            OMGBTC.SubscribeToNewCandle(this, TfEnter.frame);
            OMGBTC.SubscribeToNewCandle(this, TfExit.frame);

            TfEnter.ticks = OMGBTC.GetNavigator(TfEnter.frame);
            TfExit.ticks = OMGBTC.GetNavigator(TfExit.frame);
            BollingerBands = new BollingerBands("Boll5m", Boll_Period, Boll_Dev, TfEnter.ticks);
            BollNavigator = BollingerBands.GetNavigator();

            MeanAndVariance = new MeanAndVariance(100, TfEnter.ticks);
            MeanNavigator = MeanAndVariance.GetNavigator();
        }

        bool FirstPass = true;

        private void OMGBTC_OnTick(ISymbolFeed obj)
        {
            BollingerBands.Calculate();
            MeanAndVariance.Calculate();
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

                if (TfEnter.ticks.Next())
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

        private void SearchEnter()
        {
            if (BollingerBands.IsReady)
            {
                BollNavigator.SeekLast();
                MeanNavigator.SeekLast();
                var bollTick = BollNavigator.Tick;
                var bollTick1 = BollNavigator.GetFromCursor(1);
                var candle = TfExit.ticks.LastTick;

                var growing = (MeanNavigator.Tick.Mean - MeanNavigator.PreviousTick.Mean) / MeanNavigator.Tick.Mean > 0.001;
                var closeUnderDev = candle.Close < bollTick.Bottom;
                var stdvHi = bollTick.Deviation / candle.Close > 0.02;

                if (growing && closeUnderDev && stdvHi)
                {
                    Binance.MarketOrder(OMGBTC.Symbol, TradeType.Buy, GetAmountToTrade());
                    Status = BotStatus.SearchExit;
                    EnterPrice = candle.Close;
                }

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
                if (closeOnTop)
                {
                    
                    Binance.MarketOrder(OMGBTC.Symbol, TradeType.Sell, Binance.GetBalance(OMGBTC.Asset));
                    Status = BotStatus.SearchEnter;
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
