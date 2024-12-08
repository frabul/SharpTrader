
import { createChart, IChartApi, TimeRange, MouseEventParams, Point, Coordinate, BarPrice, BarData, BarPrices } from 'lightweight-charts';


class Figure {
    Chart: IChartApi;
    LastCrosshairForcedPoint: Point;
    Container: HTMLElement;
    LegendFirstRow: HTMLDivElement;
    constructor(chart: IChartApi, container: HTMLElement) {
        this.Chart = chart;
        this.Container = container;
        this.LastCrosshairForcedPoint = { x: 0 as Coordinate, y: 0 as Coordinate };
    }
}

var charts: Figure[] = [];
var lastVisibleRangeSet: TimeRange = { from: "", to: "" };

function AddCandlesSerie(figure: Figure, seriesData: any) {
    var lineSeries = figure.Chart.addCandlestickSeries({
        priceLineVisible: false,
        lastValueVisible: true,
        baseLineVisible: false
    });
    lineSeries.setData(seriesData.Points);
    lineSeries.setMarkers(seriesData.Markers);




    figure.Chart.subscribeCrosshairMove((param) => {
        if (param.time) {
            var pointedElem: BarPrices = param.seriesPrices.get(lineSeries) as BarPrices;
            if (pointedElem != undefined)
                figure.LegendFirstRow.innerText = `${seriesData.Name} - O:${pointedElem.open.toFixed(7)} - H:${pointedElem.high.toFixed(7)}`;
        }
        else {
            figure.LegendFirstRow.innerText = seriesData.Name;
        }
    });
}

function AddLineSerie(chart: IChartApi, series: any) {
    var lineSeries = chart.addLineSeries({
        priceLineVisible: false,
        lastValueVisible: false
    });

    lineSeries.applyOptions(series.Options)
    lineSeries.setData(series.Points);
    lineSeries.setMarkers(series.Markers);
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
                // timeFormatter: businessDayOrTimestamp => {
                //     if (typeof (businessDayOrTimestamp) == 'number') {
                //         var date = new Date(businessDayOrTimestamp * 1000);
                //         return date.toTimeString();
                //     } else
                //         return businessDayOrTimestamp;
                // },
                priceFormatter: price => { if (price >= 0) return "+" + price.toFixed(7); else return price.toFixed(7); }
            },
            rightPriceScale: {
                drawTicks: false
            },
            timeScale: {
                timeVisible: true,
                secondsVisible: false
            }
        }
    )
    var figure = new Figure(chart, box);
    charts.push(figure);

    figureData.Series.forEach(series => {
        switch (series.Type) {
            case "Candlestick":
                AddCandlesSerie(figure, series);
                break;
            case "Line":
                AddLineSerie(chart, series);
                break;
        }
    });


    function onVisibleTimeRangeChanged(newVisibleTimeRange: TimeRange) {

        var now = new Date();
        //if (now > inhibitSynchUntil) {
        if (lastVisibleRangeSet.from != newVisibleTimeRange.from || lastVisibleRangeSet.to != newVisibleTimeRange.to) {
            charts.forEach(e => {
                if (e.Chart != chart)
                    e.Chart.timeScale().setVisibleRange(newVisibleTimeRange)
            });
            lastVisibleRangeSet = newVisibleTimeRange;
        }
    }

    chart.timeScale().subscribeVisibleTimeRangeChange(onVisibleTimeRangeChanged);

    chart.subscribeCrosshairMove(function (par: MouseEventParams) {
        charts.forEach(c => {
            if (c.Chart != chart && par.point != undefined) {
                if (c.LastCrosshairForcedPoint.x != par.point.x
                    || c.LastCrosshairForcedPoint.y != par.point.y) {

                    c.LastCrosshairForcedPoint = par.point;
                    c.Chart.setCrosshair(par.point.x, par.point.y);
                }
            }
        });
    });

    //-- create legend --
    var legend = document.createElement('div');
    legend.classList.add('legend');
    figure.Container.appendChild(legend);

    var firstRow = document.createElement('div');
    firstRow.innerText = '';
    legend.appendChild(firstRow);
    figure.LegendFirstRow = firstRow;
    //-- start auto resize --
    var intervalId = setInterval(function () {
        chart.resize(box.clientWidth, box.clientHeight);
    }, 200);
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



