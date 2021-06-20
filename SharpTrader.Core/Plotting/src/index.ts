import { chartData } from './data';
import { createChart, IChartApi } from 'lightweight-charts';

chartData.figures[0].series[1].points.forEach(point => {
    point.value = point.value * 1.1;
});

var figuresCount = 0;

function AddCandlesSerie(chart: IChartApi, seriesData: any) {
    var lineSeries = chart.addCandlestickSeries();
    lineSeries.setData(seriesData.Points);
}

function AddLineSerie(chart: IChartApi, series: any) {
    var lineSeries = chart.addLineSeries();
    lineSeries.setData(series.Points);
}

function CreateFigure(figureData) {
    var box = document.getElementById("chartBox").getElementsByTagName("div")[figuresCount++];   // Get the element with id="demo"
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
               
            },
            timeScale:{
                timeVisible: true
            }
        }
    )
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


}
//   create a serie on the charts ( of required type0)
//   add data



var request = new XMLHttpRequest();
request.open("GET", "chart.json", false);
request.send(null);
var jsonData = JSON.parse(request.responseText);

//create the main chart
//for each serie that is in main area
jsonData.Figures.forEach(CreateFigure);

