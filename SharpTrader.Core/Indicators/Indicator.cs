using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Indicators
{
    public abstract class Indicator
    {
        public string Name { get; private set; }
        public Indicator(string name)
        {
            Name = name;
        }
        public abstract bool IsReady { get; }
    }
}
