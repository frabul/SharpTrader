using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{



    public class Signal<T> : TimeSerieNavigator<T>, ISignalNavigator where T : ITimeRecord
    {
        public event Action OnNewSample;
         
        private Func<T, double> Selector;
         
        public double this[int i] => Selector(GetFromLast(i));

        public Signal(TimeSerieNavigator<T> backSerie, Func<T, double> selector) :
            base(backSerie)
        {
            Selector = selector;
            this.OnNewRecord += rec => OnNewSample?.Invoke(); 
        }

      
    }

}
