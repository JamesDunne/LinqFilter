using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WellDunne.Extensions
{
    public struct LineInfo
    {
        private readonly int _LineNumber;
        public int LineNumber { get { return _LineNumber; } }
        private readonly string _Text;
        public string Text { get { return _Text; } }

        public LineInfo(int lineNumber, string text)
        {
            _LineNumber = lineNumber;
            _Text = text;
        }
    }
}
