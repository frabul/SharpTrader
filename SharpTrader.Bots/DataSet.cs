using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    class MLDataSet
    {
        public List<Record> Records;

        public class Record
        {
            public int Id;
            public List<double> Features;
            public List<double> Labels;

        }

    }
}
