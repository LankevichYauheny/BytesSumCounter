using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BytesSumCounter
{
    public class BigFile
    {
        public string FileName { get; set; }
        public float Size { get; set; }
        public int Unit { get; set; }
        public int SumResult { get; set; }
        public bool IsFinished { get; set; }
    }
}
