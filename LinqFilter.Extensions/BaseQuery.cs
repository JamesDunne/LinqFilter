using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace LinqFilter.Extensions
{
    public abstract class BaseQuery
    {
        protected sealed class FormatArgs
        {
            public string Format { get; private set; }
            public object[] Args { get; private set; }

            public FormatArgs(string format, params object[] args)
            {
                this.Format = format;
                this.Args = args;
            }
        }

        protected static bool Warning(bool condition, Func<FormatArgs> formatMessage)
        {
            if (!condition)
            {
                FormatArgs args = formatMessage();
                Console.Error.WriteLine(args.Format, args.Args);
            }
            return condition;
        }

        protected static bool Error(bool condition, Func<FormatArgs> formatMessage)
        {
            if (!condition)
            {
                FormatArgs args = formatMessage();
                Console.Error.WriteLine(args.Format, args.Args);
                throw new Exception(String.Format(args.Format, args.Args));
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

        protected static MaybeException<T> Try<T>(Func<T> throwableAction)
        {
            try
            {
                return throwableAction();
            }
            catch (Exception ex)
            {
                return (MaybeException<T>)ex;
            }
        }
    }
}
