using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    public class TestBot2 : TraderBot
    {
        private bool FirstPass = true;

        private bool Started = false;
        private IMarketApi Binance;
        private ISymbolFeed TradeSymbolFeed;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfEnter;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfExit;
        private BollingerBands BollingerBands;
        private MeanAndVariance MeanAndVariance;
        private TimeSerieNavigator<MeanAndVariance.Record> MeanNavigator;
        private MeanAndVariance MeanAndVarianceShort;
        private TimeSerieNavigator<BollingerBands.Record> BollNavigator;
        public DataSetCreator DataSetCreator { get; set; }

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
            public int FeaturesId;
            public double EnterPrice;
        }
        List<Position> OpenPositions = new List<Position>();

        public TestBot2(IMarketsManager markets) : base(markets)
        {

        }

        public override void Start()
        {
            Started = true;
            TfEnter.frame = TimeframeEnter;
            TfExit.frame = TimeframeExit;

            Binance = MarketsManager.GetMarketApi(Market);
            GraphFeed = TradeSymbolFeed = Binance.GetSymbolFeed(TradeSymbol);


            TradeSymbolFeed.OnTick += OMGBTC_OnTick;
            RefSymbolFeed = Binance.GetSymbolFeed("BTCUSDT");

            TradeSymbolFeed.SubscribeToNewCandle(this, TfEnter.frame);
            TradeSymbolFeed.SubscribeToNewCandle(this, TfExit.frame);

            TfEnter.ticks = TradeSymbolFeed.GetNavigator(TfEnter.frame);
            TfExit.ticks = TradeSymbolFeed.GetNavigator(TfExit.frame);

            BollingerBands = new BollingerBands("Boll5m", Boll_Period, Boll_Dev, TfEnter.ticks);
            BollNavigator = BollingerBands.GetNavigator();

            MeanAndVariance = new MeanAndVariance(130, TfEnter.ticks);
            MeanNavigator = MeanAndVariance.GetNavigator();
            MeanAndVarianceShort = new MeanAndVariance(40, TfEnter.ticks);
            Drawer.Candles = new TimeSerieNavigator<ICandlestick>(TfEnter.ticks);

            Drawer.Lines.AddRange(new[] { LineBollBot, LineBollMid, LineBollTop });

            DataSetCreator = new DataSetCreator();
            DataSetCreator.Navigators = new TimeSerieNavigator<ICandlestick>[]
            {
                RefSymbolFeed.GetNavigator(TimeSpan.FromMinutes(5)),
                TradeSymbolFeed.GetNavigator(TimeSpan.FromMinutes(5)),
                TradeSymbolFeed.GetNavigator(TimeSpan.FromMinutes(120)),
            };
            DataSetCreator.Means = new(TimeSerieNavigator<MeanAndVariance.Record> data, int steps)[]
            {
               (MeanAndVariance.GetNavigator(), MeanAndVariance.Period),
               (MeanAndVarianceShort.GetNavigator() , MeanAndVarianceShort.Period)
            };
        }

        private void OMGBTC_OnTick(ISymbolFeed obj)
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
        private ISymbolFeed RefSymbolFeed;

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
                    var stdvHi = (bollTick.Main - candle.Close) / candle.Close > 0.02;
                    if (closeUnderDev && stdvHi)
                    {
                        try
                        {
                            var featuresID = DataSetCreator.CalculateFeatures();
                            Binance.MarketOrder(TradeSymbolFeed.Symbol, TradeType.Buy, GetAmountToTrade());
                            OpenPositions.Add(new Position()
                            {
                                EnterPrice = candle.Close,
                                FeaturesId = featuresID
                            });

                            TradeLine = new Line();
                            TradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
                        }
                        catch
                        {

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

                var closeOnMean = candle.Close >= bollTick.Main;
                var closeOnTop = candle.Close >= bollTick.Top;
                var closeOnMidway = candle.Close >= bollTick.Main + bollTick.Deviation / 2;
                if (closeOnMidway)
                {

                    Binance.MarketOrder(TradeSymbolFeed.Symbol, TradeType.Sell, Binance.GetBalance(TradeSymbolFeed.Asset));

                    foreach (var op in OpenPositions)
                    {
                        if (op.EnterPrice > candle.Close)
                            DataSetCreator.SetLabel(op.FeaturesId, new float[] { 1 });
                        else
                            DataSetCreator.SetLabel(op.FeaturesId, new float[] { 0 });

                        TradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
                        TradeLine.Color = op.EnterPrice > candle.Close ?
                            new ColorARGB(255, 255, 10, 10) : new ColorARGB(255, 10, 10, 255);
                        Drawer.Lines.Add(TradeLine);
                    }
                    OpenPositions.Clear();
                }

            }

        }


    }


    public class DataSetCreator
    {
        public MLDataSet Data { get; set; } = new MLDataSet();

        


        public int CandlesCount { get; set; } = 25;

        public TimeSerieNavigator<ICandlestick>[] Navigators { get; set; }
        public (TimeSerieNavigator<MeanAndVariance.Record> data, int steps)[] Means { get; set; }

        double CurrentPrice = 1;

        float Normalize(float val)
        {
            return (float)(val / CurrentPrice);
        }
        float Normalize(double val)
        {
            return (float)(val / CurrentPrice);
        }
        public int CalculateFeatures()
        {
            foreach (var m in Means)
                m.data.SeekLast();
            foreach (var n in Navigators)
                n.SeekLast();

            var record = new MLDataSet.Record(Data.GetNextId());

            //Data.Records.Add()

            //20 candles from tf1, high low close, normalized to previous
            //20 candles from tf2
            //20 candles from reference symbol
            //Mean(80): Mean[0] - Mean[80], Mean[0] ; normalized respect to last price
            //Mean(40): Mean[0] - Mean[80], Mean[0]
            List<ICandlestick>[] candles = new List<ICandlestick>[Navigators.Length];
            var inputTensor = new float[Navigators.Length][][];
            int it = 0;
            foreach (var nav in Navigators)
            {
                var cand = new List<ICandlestick>();
                float[][] mat = new float[CandlesCount + Means.Length][];

                CurrentPrice = nav.Tick.Close;
                int i = 0;
                //-------- add the means
                foreach (var m in Means)
                {
                    var m1 = m.data.Tick.Mean;
                    var m2 = m.data.GetFromCursor(m.steps).Mean;
                    float[] arr = new float[] {
                        Normalize(m1) ,
                        Normalize(m2),
                        Normalize(m1 - m2)
                    };
                    mat[i++] = arr;
                }
                //add candles data
                while (cand.Count < CandlesCount)
                {
                    var candle = nav.Tick;
                    mat[i++] = new[]
                    {
                       Normalize(candle.Open  ),
                       Normalize(candle.High ),
                       Normalize(candle.Low ),
                    };

                    cand.Add(nav.Tick);
                    nav.Previous();
                }

                inputTensor[it++] = mat;
            }
            record.Features = inputTensor;
            Data.Records.Add(record);
            return record.Id;
        }

        public void SetLabel(int id, float[] label)
        {
            Data.Records[id].Labels = label;
        }


    }
}
