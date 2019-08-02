using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BytesSumCounter
{
    public class File4Calc
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public bool IsFinished { get; set; }
        public Task<ulong> task;
    }
}
