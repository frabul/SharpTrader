using SharpTrader.Indicators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    public class TestBot2 : TraderBot
    {
        private bool FirstPass = true;

        private IMarketApi Binance;
        private ISymbolFeed TradeSymbolFeed;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfEnter;
        private (TimeSpan frame, TimeSerieNavigator<ICandlestick> ticks) TfExit;
        private BollingerBands BollingerBands;
        private MeanAndVariance MeanAndVariance;
        private TimeSerieNavigator<MeanAndVariance.Record> MeanNavigator;
        private MeanAndVariance MeanAndVarianceShort;
        private TimeSerieNavigator<BollingerBands.Record> BollNavigator;

        private Line LineBollTop = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        private Line LineBollMid = new Line() { Color = new ColorARGB(255, 0, 150, 150) };
        private Line LineBollBot = new Line() { Color = new ColorARGB(255, 0, 150, 150) };

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
            public DateTime Time;
            public int FeaturesId;
            public double EnterPrice;
        }
        List<Position> OpenPositions = new List<Position>();

        public TestBot2(IMarketsManager markets, DataSetCreator ds) : base(markets)
        {
            DataSetCreator = ds;
        }

        public override void Start()
        {
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

        private decimal GetAmountToTrade()
        {
            //double portFolioValue = Binance.GetBtcPortfolioValue();
            var price = TfEnter.ticks.Tick.Close;
            if (OpenPositions.Count >= 30)
                return Binance.GetBalance("BTC");
            else
                return (Binance.GetBalance("BTC") / (30 - OpenPositions.Count)) / (decimal)price;
        }

        public override void OnNewCandle(ISymbolFeed sender, ICandlestick newCandle)
        {

        }


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
                    if (closeUnderDev && stdvHi)
                    {
                        try
                        {
                            var toTrade = GetAmountToTrade();
                            if (toTrade <= 0)
                                return;
                            var featuresID = DataSetCreator.CalculateFeatures();
                            Binance.MarketOrder(TradeSymbolFeed.Symbol, TradeType.Buy, toTrade);
                            OpenPositions.Add(new Position()
                            {
                                Time = candle.Time,
                                EnterPrice = candle.Close,
                                FeaturesId = featuresID
                            });
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
                        var tradeLine = new Line();
                        tradeLine.Points.Add(new Point(op.Time, op.EnterPrice));
                        var btcWon = (1d / op.EnterPrice) * (candle.Close - op.EnterPrice);

                        if ((op.EnterPrice * 1.003) > candle.Close)
                        {
                            tradeLine.Color = new ColorARGB(255, 255, 10, 10);
                            DataSetCreator.SetLabel(op.FeaturesId, new float[] { 0, (float)btcWon });
                        }

                        else
                        {
                            tradeLine.Color = new ColorARGB(255, 10, 10, 255);
                            DataSetCreator.SetLabel(op.FeaturesId, new float[] { 1, (float)btcWon });
                        }
                        tradeLine.Points.Add(new Point(candle.CloseTime, candle.Close));
                        Drawer.Lines.Add(tradeLine);
                    }
                    OpenPositions.Clear();
                }

            }

        }
    }


    public class DataSetCreator
    {
        public MLDataSet Data { get; set; } = new MLDataSet();
        public int CandlesCount { get; set; } = 20;

        public TimeSerieNavigator<ICandlestick>[] Navigators { get; set; }
        public (TimeSerieNavigator<MeanAndVariance.Record> data, int steps)[] Means { get; set; }



        //float Normalize(float val)
        //{
        //    return (float)(val / CurrentPrice);
        //}

        float Normalize(double val, double price)
        {
            return (float)(val / price);
        }

        public int CalculateFeatures()
        {
            //find the smaller timeFrame 
            foreach (var m in Means)
            {
                m.data.SeekLast();
                if (m.data.Count < m.steps + 1)
                    return -1;
            }
            foreach (var n in Navigators)
            {
                n.SeekLast();
                if (n.Count < 50)
                    return -1;
            }

            Navigators = Navigators.OrderBy(nav => nav.Tick.Timeframe).ToArray();


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
                //se il timeframe non è il più basso, lo portiamo un passo indietro rispetto al tempo corrente
                if (Navigators[0].Tick.Timeframe != nav.Tick.Timeframe)
                {
                    if (Navigators[0].Tick.Time <= nav.Tick.Time)
                        nav.Previous();
                }

                float[][] mat = new float[CandlesCount + Means.Length * 3][];

                double CurrentPrice = nav.Tick.Close;
                int i = 0;
                //-------- add the means
                foreach (var m in Means)
                {
                    var m1 = m.data.Tick.Mean;
                    var m2 = m.data.GetFromCursor(m.steps).Mean;
                    float[] arr = new float[] {
                        Normalize(m1, CurrentPrice) ,
                        Normalize(m2,CurrentPrice),
                        Normalize(m1 - m2,CurrentPrice)
                    };
                    //mat[i++] = arr;
                    mat[i++] = new float[] { arr[0], 0, 0 };
                    mat[i++] = new float[] { arr[1], 0, 0 };
                    mat[i++] = new float[] { arr[2], 0, 0 };
                }
                //add candles data
                var cand = new List<ICandlestick>();
                while (cand.Count < CandlesCount)
                {
                    var candle = nav.Tick;
                    mat[i++] = new[]
                    {
                       Normalize(candle.Open,CurrentPrice  ),
                       Normalize(candle.High,CurrentPrice ),
                       Normalize(candle.Low,CurrentPrice ),
                    };

                    cand.Add(nav.Tick);
                    nav.Previous();
                }

                inputTensor[it++] = mat;
            }
            record.Features = inputTensor;
            record.Id = Data.Records.Count;
            Data.Records.Add(record);
            Debug.Assert(Data.Records.Count == record.Id + 1);
            return record.Id;
        }

        public void SetLabel(int id, float[] label)
        {
            if (id > -1)
                Data.Records[id].Labels = label;
        }


    }
}
