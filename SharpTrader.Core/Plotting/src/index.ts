import { chartData } from './data';
import { createChart, IChartApi, TimeRange, MouseEventParams } from 'lightweight-charts';

var figuresCount = 0;
var charts: IChartApi[] = [];
var inhibitSynchUntil: Date = new Date();
var lastVisibleRangeSet: TimeRange = { from: "", to: "" };

chartData.figures[0].series[1].points.forEach(point => {
    point.value = point.value * 1.1;
});

function AddCandlesSerie(chart: IChartApi, seriesData: any) {
    var lineSeries = chart.addCandlestickSeries({
        priceLineVisible: false,
        lastValueVisible: false
    });
    lineSeries.setData(seriesData.Points);
}

function AddLineSerie(chart: IChartApi, series: any) {
    var lineSeries = chart.addLineSeries({
        color: series.Color,
        lineWidth: 1,
        priceLineVisible: false,
        lastValueVisible: false
    });
    lineSeries.applyOptions({ color: series.Color })
    lineSeries.setData(series.Points);
}


function CreateFigure(figureData) {
    var container = document.getElementById("chartBox");   // Get the element with id="demo"
    var box = document.createElement('div');
    container.appendChild(box)
    box.className = "grid-item";
    container.style.gridTemplateRows += " " + figureData.HeightRelative + "fr";
    var chart = createChart(box, { width: box.clientWidth, height: box.clientHeight });
    chart.applyOptions(
        {
            localization: {
                timeFormatter: businessDayOrTimestamp => {
                    if (typeof (businessDayOrTimestamp) == 'number') {
                        var date = new Date(businessDayOrTimestamp * 1000);
                        return date.toTimeString();
                    } else
                        return businessDayOrTimestamp;
                },
                priceFormatter: price => price.toFixed(7)
            },

            timeScale: {
                timeVisible: true
            }
        }
    )


    function onVisibleTimeRangeChanged(newVisibleTimeRange: TimeRange) {

        var now = new Date();
        //if (now > inhibitSynchUntil) {
        if (lastVisibleRangeSet.from != newVisibleTimeRange.from || lastVisibleRangeSet.to != newVisibleTimeRange.to) {
            inhibitSynchUntil = new Date(now.getTime() + 50);
            charts.forEach(e => {
                if (e != chart)
                    e.timeScale().setVisibleRange(newVisibleTimeRange)
            });
            lastVisibleRangeSet = newVisibleTimeRange;
        }
    }

    chart.timeScale().subscribeVisibleTimeRangeChange(onVisibleTimeRangeChanged);
    figureData.Series.forEach(series => {
        switch (series.Type) {
            case "Candlestick":
                AddCandlesSerie(chart, series);
                break;
            case "Line":
                AddLineSerie(chart, series);
                break;
        }
    });


    chart.options
    var intervalId = setInterval(function () {
        chart.resize(box.clientWidth, box.clientHeight);
    }, 250);

    charts.push(chart);
}

var searchParams = new URLSearchParams(window.location.search);
var chartFile = searchParams.get('chart');
var request = new XMLHttpRequest();
request.open("GET", chartFile, false);
request.send(null);
var jsonData = JSON.parse(request.responseText);

//create the main chart
//for each serie that is in main area
jsonData.Figures.forEach(CreateFigure);

let theChart = charts[0];
charts[0].subscribeCrosshairMove(function (par: MouseEventParams) {
    charts.forEach(c => {
        if (c != theChart)
            c.setCrosshair(par.point.x, par.point.y)
    });
});

 