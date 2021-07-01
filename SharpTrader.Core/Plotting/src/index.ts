
import { createChart, IChartApi, TimeRange, MouseEventParams, Point, Coordinate, BarPrice, BarData, BarPrices, ISeriesApi } from 'lightweight-charts';

class Figure {
    Chart: IChartApi;
    LastCrosshairForcedPoint: Point;
    Container: HTMLElement;
    Legend: HTMLDivElement;
    SeriesMap: Map<any, any>;
    constructor(chart: IChartApi, container: HTMLElement) {
        this.Chart = chart;
        this.Container = container;
        this.LastCrosshairForcedPoint = { x: 0 as Coordinate, y: 0 as Coordinate };
        this.SeriesMap = new Map();
    }
}

var figures: Figure[] = [];
var lastVisibleRangeSet: TimeRange = { from: "", to: "" };

function AddCandlesSerie(figure: Figure, seriesData: any) {
    var series = figure.Chart.addCandlestickSeries({
        priceLineVisible: false,
        lastValueVisible: true,
        baseLineVisible: false,
        priceFormat: { precision: 7, minMove: 0.000001 }
    });
    series.setData(seriesData.Points);
    series.setMarkers(seriesData.Markers);
    figure.SeriesMap.set(series, seriesData);
}

function AddLineSerie(figure: Figure, seriesData: any) {
    var chart = figure.Chart;
    let series = chart.addLineSeries({
        priceLineVisible: false,
        lastValueVisible: false,
        priceFormat: { precision: 7, minMove: 0.000001 }
    });

    series.applyOptions(seriesData.Options)
    series.setData(seriesData.Points);
    series.setMarkers(seriesData.Markers);
    figure.SeriesMap.set(series, seriesData);
}

function CreateFigure( figureData, box:HTMLDivElement ) { 
   
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
    figures.push(figure);

    figureData.Series.forEach(series => {
        switch (series.Type) {
            case "Candlestick":
                AddCandlesSerie(figure, series);
                break;
            case "Line":
                AddLineSerie(figure, series);
                break;
        }
    });

    function onVisibleTimeRangeChanged(newVisibleTimeRange: TimeRange) {

        var now = new Date();
        //if (now > inhibitSynchUntil) {
        if (lastVisibleRangeSet.from != newVisibleTimeRange.from || lastVisibleRangeSet.to != newVisibleTimeRange.to) {
            figures.forEach(e => {
                if (e.Chart != chart)
                    e.Chart.timeScale().setVisibleRange(newVisibleTimeRange)
            });
            lastVisibleRangeSet = newVisibleTimeRange;
        }
    }

    //-- create legend --
    figure.Legend = document.createElement('div');
    figure.Legend.classList.add('legend');
    figure.Container.appendChild(figure.Legend);


    //---- crosshair handling ---
    chart.timeScale().subscribeVisibleTimeRangeChange(onVisibleTimeRangeChanged);

    chart.subscribeCrosshairMove(function (par: MouseEventParams) {
        // synch all figures
        figures.forEach(c => {
            if (c.Chart != chart && par.point != undefined) {
                if (c.LastCrosshairForcedPoint.x != par.point.x
                    || c.LastCrosshairForcedPoint.y != par.point.y) {

                    c.LastCrosshairForcedPoint = par.point;
                    c.Chart.setCrosshair(par.point.x, par.point.y);
                }
            }
        });
        // add legend
        figure.Legend.innerHTML = "";
        if (par.time) {
            par.seriesPrices.forEach((v, series) => {
                let ser = figure.SeriesMap.get(series);
                let row = document.createElement('div');
                figure.Legend.appendChild(row);
                if (ser.Type == "Candlestick") {
                    let pointedVal: BarPrices = v as BarPrices;
                    if (pointedVal != undefined)
                        row.innerText = `${ser.Name} - O: ${pointedVal.open.toFixed(7)} - H: ${pointedVal.high.toFixed(7)} - L: ${pointedVal.low.toFixed(7)} - C: ${pointedVal.close.toFixed(7)}`;
                } else {
                    let pointedVal: BarPrice = v as BarPrice;
                    if (pointedVal != undefined)
                        row.innerText = `${ser.Name} - Y: ${pointedVal.toFixed(7)}`;
                }
            });
        }
    });

    //-- start auto resize --
    var intervalId = setInterval(function () {
        chart.resize(box.clientWidth, box.clientHeight);
    }, 200);
}

function CreateChart(chartData) { 
    let figures : Array<any> = chartData.Figures;
    var container = document.getElementById("chartBox");   
    container.innerHTML = "";
    container.style.gridTemplateRows = "";

    figures.forEach( figData => { 
        var box = document.createElement('div');
        box.className = "grid-item";  
        container.appendChild(box);
        container.style.gridTemplateRows += " " + figData.HeightRelative + "fr"; 
        CreateFigure(figData, box); 
    });
}

function openFile(e) {
    document.getElementById('inputFile').click();
}

function readSingleFile(e: Event) {
    var file = (e as any).target.files[0];
    if (!file) {
        return;
    }
    var reader = new FileReader();
    reader.onload = function (e) {
        var contents = e.target.result;
        var jsonData = JSON.parse(contents as string); 
        //create the main chart 
        CreateChart(jsonData)
    };
    reader.readAsText(file)
}

function displayContents(contents) {
    var element = document.getElementById('file-content');
    element.textContent = contents;
}

document.getElementById('buttonOpenFile').addEventListener("click", openFile, false);
document.getElementById('inputFile').addEventListener("change", readSingleFile, false);

// var searchParams = new URLSearchParams(window.location.search);
// var chartFile = searchParams.get('chart');
// var request = new XMLHttpRequest();
// request.open("GET", chartFile, false);
// request.send(null);
//var jsonData = JSON.parse(request.responseText);

//create the main chart
//for each serie that is in main area
//jsonData.Figures.forEach(CreateFigure);
