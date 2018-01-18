using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTrader.Bots
{
    public class MLDataSet
    {
        private int IdCounter = new int();

        public List<Record> Records { get; set; } = new List<Record>();

        internal int GetNextId()
        {
            return IdCounter++;
        }

        public void SaveToDisk(string fileNameAndPath)
        {
            var stringa = JsonConvert.SerializeObject(Records);

            File.WriteAllText(fileNameAndPath, stringa);
        }

        public class Record
        {
            public Record(int id)
            {
                Id = id;
            }
            public int Id;
            public object Features;
            public object Labels;
        }
    }
}
