using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPTests
{
    class Program
    {
        static void Main(string[] args)
        {
            var elem = 7;
            List<int> arr = new List<int>() { 1, 2, 3, 4, 8, 9, 10 };
            arr.Sort(new comparer());
            int index = arr.BinarySearch(elem, new comparer());

            if (index > -1)
                arr[index] = elem;
            else
            {
                if (~index == arr.Count)
                    arr.Add(elem);
                else
                    arr.Insert(~index, elem);
            }
        }
        class comparer : IComparer<int>
        {
            public int Compare(int x, int y)
            {
                return x - y;
            }
        }
    }
}
