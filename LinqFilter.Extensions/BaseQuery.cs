using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

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

        protected static IEnumerable<string> EnumerateLines(string path)
        {
            using (var sr = File.OpenText(path))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    yield return line;
                }
                yield break;
            }
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

        protected static string EncodeTabDelimited(string value)
        {
            StringBuilder sbResult = new StringBuilder(value.Length * 3 / 2);
            foreach (char ch in value)
            {
                if (ch == '\t') sbResult.Append("\\t");
                else if (ch == '\n') sbResult.Append("\\n");
                else if (ch == '\r') sbResult.Append("\\r");
                else if (ch == '\'') sbResult.Append("\\\'");
                else if (ch == '\"') sbResult.Append("\\\"");
                else if (ch == '\\') sbResult.Append("\\\\");
                else
                {
                    sbResult.Append(ch);
                }
            }
            return sbResult.ToString();
        }

        protected static string[] SplitTabDelimited(string line)
        {
            string[] cols = line.Split('\t');
            int length = cols.Length;
            string[] result = new string[length];
            for (int i = 0; i < length; ++i)
            {
                // Treat \0 string as null:
                if (cols[i] == "\0") result[i] = null;
                else result[i] = DecodeTabDelimited(cols[i]);
            }
            return result;
        }

        protected static string JoinTabDelimited(params string[] cols)
        {
            int length = cols.Length;
            string[] tabEncoded = new string[length];
            for (int i = 0; i < length; ++i)
            {
                if (cols[i] == null) tabEncoded[i] = "\0";
                else tabEncoded[i] = EncodeTabDelimited(cols[i]);
            }
            return String.Join("\t", tabEncoded);
        }

        protected static string JoinTabDelimited(IEnumerable<string> cols)
        {
            return JoinTabDelimited(cols.ToArray());
        }
    }
}
