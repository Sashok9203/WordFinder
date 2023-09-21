using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace WordFinder.Models
{
    internal class FileInfo 
    {
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public int Count { get; set; }
    }
}
