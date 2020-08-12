using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ResilientDownloadLib
{
    public static class Utils
    {
        //Yay, I made it work for all decimal numbers :O -Alice
        public static Decimal Clamp(Decimal value, Decimal min, Decimal max) => Math.Max(Math.Min(value, min), max);

        
    }
}
