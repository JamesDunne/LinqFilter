using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.CodeDom.Compiler;
using System.Text;
using LinqFilter.Extensions;

namespace LinqFilter
{
    class Program
    {
        static string newLine = Environment.NewLine;

        static bool AssertMoreArguments(Queue<string> argQueue, string message)
        {
            if (argQueue.Count == 0)
            {
                Console.Error.WriteLine(message);
                Environment.ExitCode = -2;
                return false;
            }
            return true;
        }

        static void DoNothing(string value)
        {
        }

        static void Main(string[] args)
        {
#if false
            // IMPROMPTU UNIT TESTS FTW!!!!
            System.Diagnostics.Stopwatch sw1 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5000000; ++i)
            {
                string value = ("hello" + i.ToString()).Case
                (
                    StringComparer.OrdinalIgnoreCase,
                    new CaseMatch<string, string>("Hello", () => "world"),
                    new CaseMatch<string, string>("world", () => "hello"),
                    new CaseMatch<string, string>(() => "lol")
                );
                DoNothing(value);
            }
            sw1.Stop();

            System.Diagnostics.Stopwatch sw2 = System.Diagnostics.Stopwatch.StartNew();
            string tmp = "hello world".Substring(0, 5);
            for (int i = 0; i < 5000000; ++i)
            {
                string value;
                switch ("hello" + i.ToString())
                {
                    case "hello": value = "world"; break;
                    case "world": value = "hello"; break;
                    default: value = "lol"; break;
                }
                DoNothing(value);
            }
            sw2.Stop();

            Console.WriteLine("Case<T>(): {0} ms", sw1.ElapsedMilliseconds);
            Console.WriteLine("switch:    {0} ms", sw2.ElapsedMilliseconds);

            // yields:
            // Case<T>(): 1501 ms
            // switch:    1102 ms
            // ... which is totally awesome for me.
            return;
#endif

            if (args.Length == 0)
            {
                DisplayUsage();
                Environment.ExitCode = -2;
                return;
            }

            // TODO: Imported files via -i option cannot include other files with a //-i comment line.
            List<string> trueArgs = new List<string>(args.Length);
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "-i")
                {
                    if (i >= args.Length - 1)
                    {
                        Console.Error.WriteLine("-i argument expects a path");
                        Environment.ExitCode = -2;
                        return;
                    }

                    // Pop the argument and load the file:
                    ++i;
                    string path = args[i];
                    string[] tmpCodeLines = File.ReadAllLines(path);
                    string lineArg = "-q";

                    // Enqueue up more arguments
                    foreach (var line in tmpCodeLines)
                    {
                        string trimLine = line.TrimStart(' ', '\t');

                        if (trimLine.StartsWith("//-"))
                        {
                            string cmtLine = trimLine.Substring(2);

                            int firstSpace = cmtLine.IndexOfAny(new char[] { ' ', '\t' });
                            firstSpace = (firstSpace >= 0) ? firstSpace : cmtLine.Length;

                            string tmpArg = cmtLine.Substring(0, firstSpace);

                            if ((tmpArg == "-q") || (tmpArg == "-pre") || (tmpArg == "-post"))
                            {
                                lineArg = tmpArg;
                            }
                            else if (firstSpace != cmtLine.Length)
                            {
                                // Queue up the option and its arguments:
                                string rest = cmtLine.Substring(firstSpace + 1);
                                trueArgs.Add(tmpArg);
                                // FIXME: we could split this `rest` with escaped quoted characters into multiple arguments...
                                trueArgs.Add(rest);
                            }
                            else
                            {
                                // Just one argument, queue it up:
                                trueArgs.Add(tmpArg);
                            }
                        }
                        else
                        {
                            // Add the line of code with an argument prefix:
                            trueArgs.Add(lineArg);
                            trueArgs.Add(line);
                        }
                    }
                }
                else
                {
                    trueArgs.Add(args[i]);
                }
            }

            // Start off a new StringBuilder with a reasonable expected capacity:
            StringBuilder sbLinq = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-q")).Sum(a => a.Length));
            StringBuilder sbPre = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-pre")).Sum(a => a.Length));
            StringBuilder sbPost = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-post")).Sum(a => a.Length));

            // Create the defaults for using-namespaces and referenced-assemblies sets:
            HashSet<string> usingNamespaces = new HashSet<string>(new string[] {
                "System",
                "System.Collections.Generic",
                "System.Linq"
            });

            HashSet<string> reffedAssemblies = new HashSet<string>(new string[] {
                "mscorlib.dll",
                "System.dll",
                "System.Core.dll",
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "LinqFilter.Extensions.dll")
            }, StringComparer.InvariantCultureIgnoreCase);

            List<string> linqArgList = new List<string>(trueArgs.Count / 2);
            bool useLineInfo = false;

            // Process cmdline arguments:
            Queue<string> argQueue = new Queue<string>(trueArgs);
            while (argQueue.Count > 0)
            {
                string arg = argQueue.Dequeue();
                if (!arg.StartsWith("-"))
                {
                    Console.Error.WriteLine("All arguments must be preceded by an option!");
                    Environment.ExitCode = -2;
                    return;
                }

                if (arg == "-q")
                {
                    if (!AssertMoreArguments(argQueue, "-q option expects a single argument")) return;

                    // Append the argument as a line of code in the LINQ query:
                    string line = argQueue.Dequeue();
                    sbLinq.AppendLine(line);
                }
                else if (arg == "-pre")
                {
                    if (!AssertMoreArguments(argQueue, "-pre option expects a single argument")) return;

                    // Append the argument as a line of code in the LINQ query:
                    string line = argQueue.Dequeue();
                    sbPre.AppendLine(line);
                }
                else if (arg == "-post")
                {
                    if (!AssertMoreArguments(argQueue, "-post option expects a single argument")) return;

                    // Append the argument as a line of code in the LINQ query:
                    string line = argQueue.Dequeue();
                    sbPost.AppendLine(line);
                }
                else if (arg == "-a")
                {
                    if (!AssertMoreArguments(argQueue, "-a option expects a string argument")) return;

                    // Pop the argument and add it to the args list:
                    string sa = argQueue.Dequeue();
                    linqArgList.Add(sa);
                }
                else if (arg == "-r")
                {
                    if (!AssertMoreArguments(argQueue, "-r option expects an assembly name")) return;

                    // Pop the argument and add it to the referenced-assemblies set:
                    string ra = argQueue.Dequeue();
                    if (!reffedAssemblies.Contains(ra))
                        reffedAssemblies.Add(ra);
                }
                else if (arg == "-u")
                {
                    if (!AssertMoreArguments(argQueue, "-u option expects a namespace name")) return;

                    // Pop the argument and add it to the using-namespaces set:
                    string ns = argQueue.Dequeue();
                    if (!usingNamespaces.Contains(ns))
                        usingNamespaces.Add(ns);
                }
                else if (arg == "-nl")
                {
                    newLine = Environment.NewLine;
                }
                else if (arg == "-lf")
                {
                    newLine = "\n";
                }
                else if (arg == "-crlf")
                {
                    newLine = "\r\n";
                }
                else if (arg == "-0")
                {
                    newLine = "\0";
                }
                else if (arg == "-sp")
                {
                    newLine = " ";
                }
                else if (arg == "-pipe")
                {
                    newLine = "|";
                }
                else if (arg == "-ln")
                {
                    useLineInfo = true;
                }
                else
                {
                    Console.Error.WriteLine("Unrecognized option \"{0}\"!", arg);
                    Environment.ExitCode = -2;
                    return;
                }
            }

            // Check if we have a query to execute:
            string linqQueryCode = sbLinq.ToString();
            if (linqQueryCode.Length == 0)
            {
                DisplayUsage();
                Environment.ExitCode = -2;
                return;
            }

            // Create an IEnumerable<string> that reads stdin line by line:
            IEnumerable<string> lines = StreamLines(Console.In);
            IEnumerable<LineInfo> advLines = lines.Select((text, i) => new LineInfo(i + 1, text));

            // Create a C# v3.5 compiler provider:
            var provider = new Microsoft.CSharp.CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v3.5" } });

            // Add some basic assembly references:
            var options = new CompilerParameters();
            options.ReferencedAssemblies.AddRange(reffedAssemblies.ToArray());

            // Generate the method's code:
            string generatedCode = sbPre.ToString() + @"
        IEnumerable<string> query =
" + sbLinq.ToString() + @";
" + sbPost.ToString();

            // NOTE: Yes, I realize this is easily injectible. You should only be running this on your local machine
            // and so you implicitly trust yourself to do nothing nefarious to yourself. If you're trying to subvert
            // the mechanisms of the static method, then you've missed the point of this tool.
            string[] dynamicSources = new string[] {
                // Add the using namespace lines:
                String.Join(Environment.NewLine, (
                    from ns in usingNamespaces
                    orderby ns
                    select "using " + ns + ";"
                ).ToArray()) +
@"
public class DynamicQuery
{
    private static bool Warning(bool condition, string errorFormat, params object[] args)
    {
        if (!condition) Console.Error.WriteLine(errorFormat, args);
        return condition;
    }

    private static bool Error(bool condition, string errorFormat, params object[] args)
    {
        if (!condition)
        {
            Console.Error.WriteLine(errorFormat, args);
            throw new Exception(String.Format(errorFormat, args));
        }
        return condition;
    }

    private static IEnumerable<string> Single(string value)
    {
        return Enumerable.Repeat<string>(value, 1);
    }

    public static IEnumerable<string> GetQuery(IEnumerable<" + (useLineInfo ? "LinqFilter.Extensions.LineInfo" : "string") + @"> lines, string[] args)
    {
" + generatedCode + @"
        return query;
    }
}"
            };

            var results = provider.CompileAssemblyFromSource(options, dynamicSources);

            // Check compilation errors:
            if (results.Errors.Count > 0)
            {
                int lineOffset = 26 + usingNamespaces.Count;
                string[] linqQueryLines = StreamLines(new StringReader(generatedCode)).ToArray();

                foreach (CompilerError error in results.Errors)
                {
                    for (int i = -2; i <= 0; ++i)
                    {
                        int j = error.Line + i - lineOffset;
                        if (j < 0) continue;
                        Console.Error.WriteLine(linqQueryLines[j]);
                    }
                    Console.Error.WriteLine(new string(' ', Math.Max(0, error.Column - 1)) + "^");
                    Console.Error.WriteLine("{0} {1}: {2}", error.IsWarning ? "warning" : "error", error.ErrorNumber, error.ErrorText);
                }
                Environment.ExitCode = -1;
                return;
            }

            string[] linqArgs = linqArgList.ToArray();

            try
            {
                // Find the compiled assembly's DynamicQuery type and execute its static GetQuery method:
                var t = results.CompiledAssembly.GetType("DynamicQuery");
                IEnumerable<string> lineQuery = (IEnumerable<string>)t.GetMethod("GetQuery").Invoke(
                    null,
                    new object[2] {
                        // Select the proper IEnumerable<T> source:
                        useLineInfo ? (object)advLines : (object)lines,
                        linqArgs
                    }
                );

                // Run the filter:
                bool isFirstLine = true;
                foreach (string line in lineQuery)
                {
                    if (!isFirstLine) Console.Out.Write(newLine);
                    isFirstLine = false;

                    Console.Out.Write(line);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private static void DisplayUsage()
        {
            Console.Error.WriteLine(
@"LinqFilter.exe <options> ...

LinqFilter is a tool to dynamically compile and execute a C# LINQ expression
typed as an `IEnumerable<string>` and output the resulting items from that
query to `Console.Out`, where each item is delimited by newlines (or a custom
delimiter of your choice).

A local parameter `IEnumerable<string> lines` is given to the LINQ query
which represents an enumeration over lines read in from `Console.In`.

-q [line]      to append a line of code to the 'query' buffer (see below).
-pre [code]    to append a line of code to the 'pre' buffer (see below).
-post [cost]   to append a line of code to the 'post' buffer (see below).

-ln            `lines` becomes an `IEnumerable<LineInfo>` which gives a
               struct LineInfo {
                  int LineNumber;
                  string Text;
               }
               structure per each input line. Use this mode if you need
               line numbers along with each line of text.

-i [filename]  is used to import a section of lines of LINQ query expression
               code from a file. This option can be repeated as many times in
               order to compose larger queries from files containing partial
               bits of code.

               Comments beginning with //- are interpreted inline as
               arguments. -q, -pre, and -post change the target buffer to
               append lines from the input file to.

-u [namespace] is used to add a `using namespace;` line.
-r [assembly]  is used to add a reference to a required assembly (can be a
               path or system assembly name, must end with '.dll').

-a [arg]       to append to the `args` string[] passed to the LINQ query.

-lf            sets output delimiter to ""\n""
-crlf          sets output delimiter to ""\r\n""
-0             sets output delimiter to ""\0"" (NUL char)
-sp            sets output delimiter to "" "" (single space)
-pipe          sets output delimiter to ""|"" (vertical pipe char)
-nl            sets output delimiter to Environment.NewLine (default)

The 'query' buffer must be of the form:
   from <range variable> in lines
   ...
   select <string variable>
It should not end with a semicolon since it is an expression.

The resulting type of the query must be `IEnumerable<string>`.

The 'pre' buffer is lines of C# code placed before the LINQ query assignment
statement used in order to set up one-time local method variables and
do pre-query validation work.

The 'post' buffer is lines of C# code placed after the LINQ query assignment
statement.

EXAMPLES:
LinqFilter -q ""from line in lines select line""
will echo all input lines delimited by Environment.NewLine

LinqFilter -ln -q ""from li in lines select li.LineNumber.ToString() + "" ""
  + li.Text""
will echo all input lines prefixed by their perceived input line numbers.

LinqFilter -q ""from i in Enumerable.Range(1, 10) select i.ToString()""
will output the numbers 1 to 10 on the console delimited by Environment.NewLine
and ignore input lines.
");
        }

        private static IEnumerable<string> StreamLines(TextReader textReader)
        {
            string line;
            while ((line = textReader.ReadLine()) != null)
            {
                yield return line;
            }
            yield break;
        }
    }
}
