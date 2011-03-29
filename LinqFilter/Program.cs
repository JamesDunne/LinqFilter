using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.CodeDom.Compiler;
using System.Text;
using LinqFilter.Extensions;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;

namespace LinqFilter
{
    class Program
    {
        static void Main(string[] args)
        {
#if false
            ImpromptuUnitTest();
            return;
#endif

            if (args.Length == 0)
            {
                DisplayUsage();
                Environment.ExitCode = -2;
                return;
            }

            string codeCacheFolder = Path.Combine(Path.GetTempPath(), "LinqFilter");
            if (args[0] == "-clear-cache")
            {
                if (Directory.Exists(codeCacheFolder)) Directory.Delete(codeCacheFolder, true);
                Console.Error.WriteLine("Cleared dynamic assembly cache.");
                Environment.ExitCode = 0;
                return;
            }
            else if (args[0] == "-display-cache")
            {
                Console.Error.WriteLine(codeCacheFolder);
                Environment.ExitCode = 0;
                return;
            }

            // TODO: Imported files via -i option CANNOT include other files with a //-i comment line.
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
                "System.Linq",
                "System.Linq.Expressions",
            });

            HashSet<string> reffedAssemblies = new HashSet<string>(new string[] {
                "mscorlib.dll",
                "System.dll",
                "System.Core.dll",
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "LinqFilter.Extensions.dll")
            }, StringComparer.InvariantCultureIgnoreCase);

            List<string> linqArgList = new List<string>(trueArgs.Count / 2);
            bool useLineInfo = false;

            // Create the `lines` IEnumerable from Console.In:
            IEnumerable<string> lines = StreamLines(Console.In);

            bool execMode = false;
            int execNumProcesses = 0;
            string execPath = null;
            string execArgs = null;

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
                else if (arg == "-skip")
                {
                    if (!AssertMoreArguments(argQueue, "-skip option expects an integer argument")) return;

                    string nStr = argQueue.Dequeue();
                    int n = Int32.Parse(nStr);
                    lines = lines.Skip(n);
                }
                else if (arg == "-take")
                {
                    if (!AssertMoreArguments(argQueue, "-take option expects an integer argument")) return;

                    string nStr = argQueue.Dequeue();
                    int n = Int32.Parse(nStr);
                    lines = lines.Take(n);
                }
                else if (arg == "-exec")
                {
                    // -exec <N> <path to EXE>
                    // Creates N process instances of the given executable and outputs each line returned from the
                    // query to each process's stdin in a round robin fashion until the query terminates. The output of
                    // LinqFilter to stdout then becomes the lines of stdout from each executed process prefixed with
                    // the slot # assigned to the process followed by a TAB '\t' character. Output lines may appear in
                    // any order by slot # but each process's output order is retained.
                    execMode = true;
                    execNumProcesses = Int32.Parse(argQueue.Dequeue());
                    execPath = argQueue.Dequeue();
                }
                else if (arg == "-execargs")
                {
                    execArgs = argQueue.Dequeue();
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
                else if (arg == "-comma")
                {
                    newLine = ",";
                }
                else if (arg == "-empty")
                {
                    newLine = String.Empty;
                }
                else if (arg == "-d")
                {
                    if (!AssertMoreArguments(argQueue, "-d option expects a delimiter argument")) return;

                    // Pop the argument and set it as the output delimiter:
                    newLine = argQueue.Dequeue();
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

            // Generate the method's code:
            // We use the Expression<> type to force the query to be compiled as an expression. This
            // explicitly disallows the usage of statements in the query. If a statement is used it
            // will generate a compiler error.
            string generatedCode = sbPre.ToString() + @"
        Expression<Func<IEnumerable<string>>> query = () => (
" + sbLinq.ToString() + @");
" + sbPost.ToString() + @"
        return query;";

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
public sealed class DynamicQuery : global::LinqFilter.Extensions.BaseQuery
{
    public static Expression<Func<IEnumerable<string>>> GetQuery(IEnumerable<" + (useLineInfo ? "LinqFilter.Extensions.LineInfo" : "string") + @"> lines, string[] args)
    {
" + generatedCode + @"
    }
}"
            };

            System.Reflection.Assembly compiledAssembly;

            // Compute the SHA1 of the generated code:
            byte[] sha1 = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(dynamicSources[0]));
            StringBuilder sbSha1 = new StringBuilder(sha1.Length * 2);
            foreach (byte b in sha1) sbSha1.AppendFormat("{0:X2}", b);
            string codeSHA1 = sbSha1.ToString();

            // Create the cached assembly directory in the system's Temp folder:
            if (!Directory.Exists(codeCacheFolder)) Directory.CreateDirectory(codeCacheFolder);
            string codeCacheFile = Path.Combine(codeCacheFolder, codeSHA1 + ".dll");

            // If we have an existing assembly, load it up:
            if (File.Exists(codeCacheFile))
            {
                // Load the cached compiled assembly from an earlier run:
                compiledAssembly = System.Reflection.Assembly.LoadFrom(codeCacheFile);
            }
            else
            {
                // Create a C# v3.5 compiler provider:
                var provider = new Microsoft.CSharp.CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v3.5" } });

                // Add some basic assembly references:
                var options = new CompilerParameters();
                options.ReferencedAssemblies.AddRange(reffedAssemblies.ToArray());
                // Output to the expected cached assembly file:
                options.OutputAssembly = codeCacheFile;
                options.IncludeDebugInformation = true;

                var results = provider.CompileAssemblyFromSource(options, dynamicSources);

                // Check compilation errors:
                if (results.Errors.Count > 0)
                {
                    int lineOffset = 5 + usingNamespaces.Count;
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

                compiledAssembly = results.CompiledAssembly;
            }

            string[] linqArgs = linqArgList.ToArray();

            try
            {
                // Create an IEnumerable<string> that reads `Console.In` line by line:
                IEnumerable<LineInfo> advLines = lines.Select((text, i) => new LineInfo(i + 1, text));

                // Find the compiled assembly's DynamicQuery type and execute its static GetQuery method:
                var t = compiledAssembly.GetType("DynamicQuery");
                Expression<Func<IEnumerable<string>>> getLineQuery = (Expression<Func<IEnumerable<string>>>)t.GetMethod("GetQuery").Invoke(
                    null,
                    new object[2] {
                        // Select the proper IEnumerable<T> source:
                        useLineInfo ? (object)advLines : (object)lines,
                        linqArgs
                    }
                );

                // Compile the expression tree to get a delegate that gives us our query:
                Func<IEnumerable<string>> retrieveQuery = getLineQuery.Compile();
                // Invoke the compiled delegate to get the actual query:
                IEnumerable<string> lineQuery = retrieveQuery();

                // Run the filter through the query and produce output lines from the string items yielded by the query:
                if (execMode)
                {
                    // Spawn N processes:
                    Thread[][] copyThread = new Thread[execNumProcesses][];
                    Process[] prcs = new Process[execNumProcesses];
                    bool[] isFirstLine = new bool[execNumProcesses];

                    for (int p = 0; p < execNumProcesses; ++p)
                    {
                        isFirstLine[p] = true;
                        
                        ProcessStartInfo psi;
                        if (execArgs != null)
                            psi = new ProcessStartInfo(execPath, execArgs);
                        else
                            psi = new ProcessStartInfo(execPath);

                        psi.RedirectStandardInput = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;

                        prcs[p] = Process.Start(psi);
                        
                        // Create a thread to read from process's stdout and write to our stdout:
                        copyThread[p] = new Thread[2];

                        int slot = p + 1;
                        copyThread[p][0] = new Thread(new ParameterizedThreadStart(StreamCopy));
                        copyThread[p][0].Start(new StreamCopyState(
                            prcs[p].StandardOutput,
                            Console.Out,
                            (line) => String.Format("{0}\t{1}", slot, line),
                            (ex) => Console.Error.WriteLine("{0}\t{1}", slot, ex.ToString())
                        ));

                        copyThread[p][1] = new Thread(new ParameterizedThreadStart(StreamCopy));
                        copyThread[p][1].Start(new StreamCopyState(
                            prcs[p].StandardError,
                            Console.Error,
                            (line) => String.Format("{0}\t{1}", slot, line),
                            (ex) => Console.Error.WriteLine("{0}\t{1}", slot, ex.ToString())
                        ));
                    }

                    // Enumerate lines of the query and output each line in a round-robin fashion to
                    // each process:
                    int currP = 0;
                    foreach (string line in lineQuery)
                    {
                        if (!isFirstLine[currP])
                            prcs[currP].StandardInput.Write(newLine);
                        else
                            isFirstLine[currP] = false;

                        prcs[currP].StandardInput.Write(line);
                        
                        // Advance to next process to write to:
                        currP = (currP + 1) % execNumProcesses;
                    }

                    // Close stdin for each process:
                    for (int p = 0; p < execNumProcesses; ++p)
                    {
                        prcs[p].StandardInput.Close();
                        copyThread[p][0].Join();
                        copyThread[p][1].Join();
                    }
                }
                else
                {
                    bool isFirstLine = true;
                    foreach (string line in lineQuery)
                    {
                        if (!isFirstLine) Console.Out.Write(newLine);
                        isFirstLine = false;

                        Console.Out.Write(line);
                    }
                }
            }
            catch (System.Reflection.TargetInvocationException tiex)
            {
                Console.Error.WriteLine(tiex.InnerException.ToString());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
            }
        }

        class StreamCopyState
        {
            public TextReader Reader { get; private set; }
            public TextWriter Writer { get; private set; }
            public Func<string, string> ProcessLine { get; private set; }
            public Action<Exception> HandleException { get; private set; }

            public StreamCopyState(TextReader reader, TextWriter writer, Func<string, string> processLine, Action<Exception> handleException)
            {
                this.Reader = reader;
                this.Writer = writer;
                this.ProcessLine = processLine;
                this.HandleException = handleException;
            }
        }

        private static void StreamCopy(object threadState)
        {
            StreamCopyState state = (StreamCopyState)threadState;
            try
            {
                string line;
                while ((line = state.Reader.ReadLine()) != null)
                {
                    string outline = line;
                    if (state.ProcessLine != null)
                        outline = state.ProcessLine(line);

                    state.Writer.WriteLine(outline);
                }
            }
            catch (Exception ex)
            {
                if (state.HandleException != null)
                    state.HandleException(ex);
            }
        }

        private static void DisplayUsage()
        {
            // Displays the error text wrapped to the console's width:
            Console.Error.Write(
                String.Join(
                    Environment.NewLine,
                    (
                        from line in new string[] {
@"LinqFilter.exe <options> ...",
@"Version " + System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString(4),
@"(C)opyright 2011 James S. Dunne, bittwiddlers.org",
@"Email = FirstName + ""."" + FirstName[0] + LastName + ""@gmail.com"";",
@"",
@"LinqFilter is a tool to dynamically compile and execute a C# LINQ expression typed as an `IEnumerable<string>` and output the resulting items from that query to `Console.Out`, where each item is delimited by newlines (or a custom delimiter of your choice).",
@"",
@"A local parameter `IEnumerable<string> lines` is given to the LINQ query which represents an enumeration over lines read in from `Console.In`.",
@"",
@"-q [line]      to append a line of code to the 'query' buffer (see below).",
@"-pre [code]    to append a line of code to the 'pre' buffer (see below).",
@"-post [code]   to append a line of code to the 'post' buffer (see below).",
@"",
@"-ln            `lines` becomes an `IEnumerable<LineInfo>` which gives a",
@"               struct LineInfo {",
@"                  int LineNumber;",
@"                  string Text;",
@"               }",
@"               structure per each input line. Use this mode if you need line numbers along with each line of text.",
@"",
@"-i [filename]  is used to import a section of lines of LINQ query expression code from a file. This option can be repeated as many times in order to compose larger queries from files containing partial bits of code.",
@"",
@"               Comments beginning with //- are interpreted inline as arguments. -q, -pre, and -post change the target buffer to append lines from the input file to. All arguments except -i are allowed in //- comment lines. One argument per line.",
@"",
@"-u [namespace] is used to add a `using namespace;` line.",
@"-r [assembly]  is used to add a reference to a required assembly (can be a path or system assembly name, must end with '.dll').",
@"",
@"-a [arg]       to append to the `args` string[] passed to the LINQ query.",
@"",
@"-skip [n]      to apply a Skip(n) to the lines IEnumerable before passing it to the LINQ query.",
@"-take [n]      to apply a Take(n) to the lines IEnumerable before passing it to the LINQ query.",
@"               Note that -skip and -take are order dependent. You can apply multiple skip and take operations in any order on the IEnumerable. Generally, a -skip [n] followed by a -take [n] is the preferred approach.",
@"",
@"-lf            sets output delimiter to ""\n""",
@"-crlf          sets output delimiter to ""\r\n""",
@"-0             sets output delimiter to ""\0"" (NUL char)",
@"-sp            sets output delimiter to "" "" (single space)",
@"-pipe          sets output delimiter to ""|"" (vertical pipe char)",
@"-empty         sets output delimiter to """" (empty string)",
@"-comma         sets output delimiter to "," (comma)",
@"-nl            sets output delimiter to Environment.NewLine (default)",
@"-d <delimiter> sets output delimiter to the provided argument",
@"",
@"-clear-cache   deletes the dynamic assembly cache folder.",
@"-display-cache displays the location of the dynamic assembly cache folder.",
@"",
@"The resulting type of the query must be `IEnumerable<string>`.",
@"",
@"The 'pre' buffer is lines of C# code placed before the LINQ query assignment statement used in order to set up one-time local method variables and do pre-query validation work.",
@"",
@"The 'post' buffer is lines of C# code placed after the LINQ query assignment statement.",
@"",
@"EXAMPLES:",
@"LinqFilter -q ""from line in lines select line""",
@"will echo all input lines delimited by Environment.NewLine",
@"",
@"LinqFilter -ln -q ""from li in lines select li.LineNumber.ToString() + \"" \"" + li.Text""",
@"will echo all input lines prefixed by their perceived input line numbers.",
@"",
@"LinqFilter -q ""from i in Enumerable.Range(1, 10) select i.ToString()"" will output the numbers 1 to 10 on the console delimited by Environment.NewLine and ignore input lines.",
@"",
@"CASE EXPRESSION:",
@"In LinqFilter.Extensions.dll (automatically imported for you) is a useful extension method that gives you a way to do a `switch` statement in an expression, generally referred to in functional programming as a case expression.",
@"",
@"Extension method signatures:",
@"U Case<T, U>(this T value, params CaseMatch<T, U>[] cases)",
@"U Case<T, U>(this T value, Func<U> defaultCase, params CaseMatch<T, U>[] cases)",
@"U Case<T, U>(this T value, IEqualityComparer<T> equalityComparer, params CaseMatch<T, U>[] cases)",
@"U Case<T, U>(this T value, IEqualityComparer<T> equalityComparer, Func<U> defaultCase, params CaseMatch<T, U>[] cases)",
@"",
@"Example:",
@"  from line in lines",
@"  where line.Length > 0",
@"  let action = line[0]",
@"  select action.Case(",
@"    () => ""UNKNOWN"", // default case",
@"    Match.Case(""A"", () => ""ADD""),",
@"    Match.Case(""D"", () => ""DELETE""),",
@"    Match.Case(""M"", () => ""MODIFY"")",
@"  )",
@"",
@"This example demonstrates how to invoke the Case extension method on any type T and have the method return a U value depending on the CaseMatch<T, U> arguments provided. Type inference is achieved via the static Match.Case method.",
@"",
@"The CaseMatch arguments are evaluated in order. There is no check to ensure unique case match values. The default case is the first parameter and is executed if no cases match."
                        }
                        // Wrap the lines to the window width:
                        from wrappedLine in line.WordWrap(Console.WindowWidth - 1)
                        select wrappedLine
                    ).ToArray()
                )
            );
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

        static void ImpromptuUnitTest()
        {
            // IMPROMPTU UNIT TESTS FTW!!!!
            System.Diagnostics.Stopwatch sw1 = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5000000; ++i)
            {
                string value = ("hello" + i.ToString()).Case(
                    StringComparer.OrdinalIgnoreCase,
                    () => "lol",    // default
                    Match.Case("hello", () => "world"),
                    Match.Case("world", () => "hello")
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

            Console.WriteLine("Case(): {0} ms", sw1.ElapsedMilliseconds);
            Console.WriteLine("switch: {0} ms", sw2.ElapsedMilliseconds);

            // yields:
            // Case(): 1501 ms
            // switch: 1102 ms
            // ... which is totally awesome for me.
        }

        static void DoNothing(string value)
        {
        }
    }
}
