import { chartData } from './data';
import { createChart, IChartApi } from 'lightweight-charts';

chartData.figures[0].series[1].points.forEach(point => {
    point.value = point.value * 1.1;
});

var figuresCount = 0;

function AddCandlesSerie(chart: IChartApi, seriesData: any) {
    var lineSeries = chart.addCandlestickSeries();
    lineSeries.setData(seriesData.points);
}

function AddLineSerie(chart: IChartApi, series: any) {
    var lineSeries = chart.addLineSeries();
    lineSeries.setData(series.points);
}

function CreateFigure(figureData) {
    var box = document.getElementById("chartBox").getElementsByTagName("div")[figuresCount++];   // Get the element with id="demo"
    var chart = createChart(box, { width: 900, height: 300 });
    figureData.series.forEach(series => {
        switch (series.type) {
            case "candles":
                AddCandlesSerie(chart, series);
                break;
            case "line":
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


//create the main chart
//for each serie that is in main area
chartData.figures.forEach(CreateFigure);

