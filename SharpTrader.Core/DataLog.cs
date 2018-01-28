using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader
{
    /// <summary>
    /// This class can be used to 
    /// </summary>
    class RobotLog
    { 
        public GraphDrawer Drawer;
        public List<RobotOperation> Operations;
    }

    class RobotOperation
    {
        public List<ITrade> Trades { get; set; }
        public DateTime TimeStart { get; set; }
        public DateTime TimeEnd { get; set; } 
    }
}
