using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{ 
    public class TimeSerieTransform<TIn,TOut> : TimeSerie<TOut> where TIn : ITimeRecord where TOut : IBaseData
    { 
        private Func<TIn, TOut> TransformFunction; 
        public TimeSerieTransform(TimeSerieNavigator<TIn> backSerie, Func<TIn, TOut> selector) :
            base()
        {
            TransformFunction = selector;
            backSerie.OnNewRecord += rec => this.AddRecord( TransformFunction(rec)); 
        } 
    }

}
