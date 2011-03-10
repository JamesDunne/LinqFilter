using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LinqFilter.Extensions
{
    public abstract class BaseQuery
    {
        protected static bool Warning(bool condition, string errorFormat, params object[] args)
        {
            if (!condition) Console.Error.WriteLine(errorFormat, args);
            return condition;
        }

        protected static bool Error(bool condition, string errorFormat, params object[] args)
        {
            if (!condition)
            {
                Console.Error.WriteLine(errorFormat, args);
                throw new Exception(String.Format(errorFormat, args));
            }
            return condition;
        }

        protected static IEnumerable<string> Single(string value)
        {
            return Enumerable.Repeat<string>(value, 1);
        }

        protected static string DecodeTabDelimited(string value)
        {
            int length = value.Length;
            StringBuilder sbDecoded = new StringBuilder(length);
            for (int i = 0; i < length; ++i)
            {
                char ch = value[i];
                if (ch == '\\')
                {
                    ++i;
                    if (i >= length)
                    {
                        // throw exception?
                        break;
                    }
                    switch (value[i])
                    {
                        case 't': sbDecoded.Append('\t'); break;
                        case 'n': sbDecoded.Append('\n'); break;
                        case 'r': sbDecoded.Append('\r'); break;
                        case '\'': sbDecoded.Append('\''); break;
                        case '\"': sbDecoded.Append('\"'); break;
                        case '\\': sbDecoded.Append('\\'); break;
                        default: break;
                    }
                }
                else sbDecoded.Append(ch);
            }
            return sbDecoded.ToString();
        }

        protected static string[] SplitTabDelimited(string line)
        {
            string[] cols = line.Split('\t');
            string[] result = new string[cols.Length];
            for (int i = 0; i < cols.Length; ++i)
            {
                result[i] = DecodeTabDelimited(cols[i]);
            }
            return result;
        }
    }
}
