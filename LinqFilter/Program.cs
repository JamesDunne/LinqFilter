using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.CodeDom.Compiler;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using WellDunne.Extensions;
using System.Configuration;

namespace LinqFilter
{
    class Program
    {
        static int consoleWidth = 80;

        static void Main(string[] args)
        {
#if false
            ImpromptuUnitTest();
            return;
#endif

            try
            {
                consoleWidth = Console.WindowWidth;
            }
            catch (System.IO.IOException)
            {
                consoleWidth = 80;
            }

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

                    if (!File.Exists(path))
                    {
                        // Try the global scripts path:
                        string globalScriptsPath = ConfigurationManager.AppSettings["GlobalScriptsPath"] ?? Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                        path = Path.Combine(globalScriptsPath, path);
                        if (!File.Exists(path))
                        {
                            Console.Error.WriteLine("Could not locate '{0}' in current directory '{1}' or global scripts path '{2}'",
                                args[i],
                                Environment.CurrentDirectory,
                                globalScriptsPath
                            );
                            Environment.ExitCode = -2;
                            return;
                        }
                    }

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

                            if ((tmpArg == "-q") || (tmpArg == "-pre") || (tmpArg == "-c"))
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
                else if ((args[i] == "-?") || (args[i] == "-help"))
                {
                    DisplayUsage();
                    return;
                }
                else if (args[i] == "-help-extensions")
                {
                    DisplayExtensionsUsage();
                    return;
                }
                else if (args[i] == "-help-examples")
                {
                    DisplayExamples();
                    return;
                }
                else
                {
                    trueArgs.Add(args[i]);
                }
            }

            // Start off a new StringBuilder with a reasonable expected capacity:
            StringBuilder sbLinq = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-q")).Sum(a => a.Length));
            StringBuilder sbPre = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-pre")).Sum(a => a.Length));
            StringBuilder sbClass = new StringBuilder(trueArgs.Where((a, i) => (i >= 1) && (!a.StartsWith("-")) && (trueArgs[i - 1] == "-c")).Sum(a => a.Length));

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
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "WellDunne.Extensions.dll")
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

                    // Append the argument as a line of code in the PRE section:
                    string line = argQueue.Dequeue();
                    sbPre.AppendLine(line);
                }
                else if (arg == "-c")
                {
                    if (!AssertMoreArguments(argQueue, "-c option expects a single argument")) return;

                    // Append the argument as a line of code in the CLASS section:
                    string line = argQueue.Dequeue();
                    sbClass.AppendLine(line);
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
                else if (arg == "-f")
                {
                    // Use the default filter query:
                    sbLinq = new StringBuilder("from line in lines select line");
                    // TODO: make sure no more -q options can be accepted.
                }
                else if (arg == "-e")
                {
                    // -e <N> <path to EXE>
                    // Creates N process instances of the given executable and outputs each line returned from the
                    // query to each process's stdin in a round robin fashion until the query terminates. The output of
                    // LinqFilter to stdout then becomes the lines of stdout from each executed process prefixed with
                    // the slot # assigned to the process followed by a TAB '\t' character. Output lines may appear in
                    // any order by slot # but each process's output order is retained.
                    execMode = true;
                    execNumProcesses = Int32.Parse(argQueue.Dequeue());
                    execPath = argQueue.Dequeue();
                }
                else if (arg == "--")
                {
                    // Take the remaining arguments as process start arguments:
                    execArgs = String.Join(" ", argQueue.ToArray());
                    break;
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
                return;
            }

            // Generate the method's code:
            // We use the Expression<> type to force the query to be compiled as an expression. This
            // explicitly disallows the usage of statements in the query. If a statement is used it
            // will generate a compiler error.
            string generatedCode = sbPre.ToString() + @"
        Expression<Func<IEnumerable<string>>> query = () => (
" + sbLinq.ToString() + @");
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
public sealed partial class DynamicQuery : global::WellDunne.Extensions.BaseQuery
{
    public static Expression<Func<IEnumerable<string>>> GetQuery(IEnumerable<" + (useLineInfo ? "WellDunne.Extensions.LineInfo" : "string") + @"> lines, string[] args)
    {
" + generatedCode + @"
    }
}

public sealed partial class DynamicQuery
{
" + sbClass.ToString() + @"
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
#if ListenWithThreads
                    Thread[][] copyThread = new Thread[execNumProcesses][];
#endif
                    Process[] prcs = new Process[execNumProcesses];
                    bool[] isFirstLine = new bool[execNumProcesses];

                    for (int p = 0; p < execNumProcesses; ++p)
                    {
                        isFirstLine[p] = true;

                        prcs[p] = new Process();

                        var psi = prcs[p].StartInfo;
                        psi.FileName = execPath;
                        if (execArgs != null) psi.Arguments = execArgs;

                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardInput = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;

#if ListenWithThreads
                        prcs[p].Start();

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
#else
                        int slot = p + 1;

                        // Set the process's StandardOutput data received event to write to our Console.Out:
                        prcs[p].OutputDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs e) =>
                        {
                            if (e.Data == null) return;
                            Console.Out.WriteLine("{0}\t{1}", slot, e.Data);
                        });

                        // Set the process's StandardError data received event to write to our Console.Error:
                        prcs[p].ErrorDataReceived += new DataReceivedEventHandler((object sender, DataReceivedEventArgs e) =>
                        {
                            if (e.Data == null) return;
                            Console.Error.WriteLine("{0}\t{1}", slot, e.Data);
                        });

                        prcs[p].Start();
                        
                        // Begin the asynchronous read operations on the process's StandardOutput and StandardError:
                        prcs[p].BeginOutputReadLine();
                        prcs[p].BeginErrorReadLine();
#endif
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
                        prcs[p].WaitForExit();

#if ListenWithThreads
                        copyThread[p][0].Join();
                        copyThread[p][1].Join();
#else
                        prcs[p].CancelOutputRead();
                        prcs[p].CancelErrorRead();
#endif
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

#if ListenWithThreads
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
#endif

        private static void DisplayHeader()
        {
            // Displays the error text wrapped to the console's width:
            Console.Error.WriteLine(
                String.Join(
                    Environment.NewLine,
                    (
                        from line in new string[] {
@"LinqFilter.exe <options> ...",
@"Version " + System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString(4),
@"(C)opyright 2011 James S. Dunne <linqfilter@bittwiddlers.org>",
@"",
@"LinqFilter is a tool to dynamically compile and execute a C# LINQ expression typed as an `IEnumerable<string>` and output the resulting items from that query to `Console.Out`, where each item is delimited by newlines (or a custom delimiter of your choice).",
@"",
@"A local parameter `IEnumerable<string> lines` is given to the LINQ query which represents an enumeration over lines read in from `Console.In`."
                        }
                        // Wrap the lines to the window width:
                        from wrappedLine in line.WordWrap(consoleWidth - 1)
                        select wrappedLine
                    ).ToArray()
                )
            );
        }

        private static void DisplayUsage()
        {
            DisplayHeader();

            string[][] prms = new string[][] {
new[] { @"" },
new[] { @"-q [line]",           @"to append a line of code to the 'query' buffer (see below)." },
new[] { @"-pre [code]",         @"to append a line of code to the 'pre' buffer (see below)." },
new[] { @"-c [code]",           @"to append a line of code to the 'class' buffer (see below)." },
new[] { @"" },
new[] { @"-f",                  @"use default filter code 'from line in lines select line'." },
new[] { @"" },
new[] { @"-e [N] [path]",       @"executes N processes given the executable path and sends output lines from the query to each process in a round-robin fashion for data-parallel execution. StandardOuput and StandardError from each process is redirected and each output line is prefixed with the process number [1..N] followed by a TAB character." },
new[] { @"-- [process args]",   @"stop processing arguments for LinqFilter and pass the rest of the command line to each process if -e is specified." },
new[] { @"" },
new[] { @"-ln",                 @"`lines` becomes an `IEnumerable<LineInfo>` which gives a" },
new[] { @"",                    @"struct LineInfo {" },
new[] { @"",                    @"  int LineNumber;" },
new[] { @"",                    @"  string Text;" },
new[] { @"",                    @"}" },
new[] { @"",                    @"structure per each input line. Use this mode if you need line numbers along with each line of text." },
new[] { @"" },
new[] { @"-i [filename]",       @"is used to import a section of lines of LINQ query expression code from a file. This option can be repeated as many times in order to compose larger queries from files containing partial bits of code." },
new[] { @"",                    @"Comments beginning with //- are interpreted inline as arguments. -q, -pre, and -class change the target buffer to append lines from the input file to. All arguments except -i are allowed in //- comment lines. One argument per line." },
new[] { @"" },
new[] { @"-u [namespace]",      @"is used to add a `using namespace;` line." },
new[] { @"-r [assembly]",       @"is used to add a reference to a required assembly (can be a path or system assembly name, must end with '.dll')." },
new[] { @"" },
new[] { @"-a [arg]",            @"to append to the `args` string[] passed to the LINQ query." },
new[] { @"" },
new[] { @"-skip [n]",           @"to apply a Skip(n) to the lines IEnumerable before passing it to the LINQ query." },
new[] { @"-take [n]",           @"to apply a Take(n) to the lines IEnumerable before passing it to the LINQ query." },
new[] { @"",                    @"Note that -skip and -take are order dependent. You can apply multiple skip and take operations in any order on the IEnumerable. Generally, a -skip [n] followed by a -take [n] is the preferred approach." },
new[] { @"" },
new[] { @"-lf",                 @"sets output delimiter to ""\n""" },
new[] { @"-crlf",               @"sets output delimiter to ""\r\n""" },
new[] { @"-nl",                 @"sets output delimiter to Environment.NewLine (default)" },
new[] { @"-0",                  @"sets output delimiter to ""\0"" (NUL char)" },
new[] { @"-sp",                 @"sets output delimiter to "" "" (single space)" },
new[] { @"-pipe",               @"sets output delimiter to ""|"" (vertical pipe char)" },
new[] { @"-empty",              @"sets output delimiter to """" (empty string)" },
new[] { @"-comma",              @"sets output delimiter to "","" (comma)" },
new[] { @"-d <delimiter>",      @"sets output delimiter to the provided argument" },
new[] { @"" },
new[] { @"-clear-cache",        @"deletes the dynamic assembly cache folder." },
new[] { @"-display-cache",      @"displays the location of the dynamic assembly cache folder." },
new[] { @"" },
new[] { @"-help",               @"displays this help text." },
new[] { @"-help-extensions",    @"displays help text on static methods and extension methods exposed for use in queries." },
new[] { @"-help-examples",      @"displays help text on example queries." },
new[] { @"" },
new[] { @"The resulting type of the query must be `IEnumerable<string>`." },
new[] { @"" },
new[] { @"The 'pre' buffer is lines of C# code placed before the LINQ query assignment statement used in order to set up one-time local method variables and do pre-query validation work." },
new[] { @"" },
new[] { @"The 'class' buffer is lines of C# code placed within the dynamic class body, where you can declare structs and inner classes that you need for your query." }
            };

            // Displays the error text wrapped to the console's width:
            int maxprmLength = prms.Where(prm => prm.Length == 2).Max(prm => prm[0].Length);

            Console.Error.WriteLine(
                String.Join(
                    Environment.NewLine,
                    (
                        from cols in prms
                        let wrap1 = (cols.Length == 1) ? null
                            : cols[1].WordWrap(consoleWidth - maxprmLength - 2)
                        let tmp = (cols.Length == 1) ? cols[0].WordWrap(consoleWidth - 1)
                            : Enumerable.Repeat(cols[0] + new string(' ', maxprmLength - cols[0].Length + 1) + wrap1.First(), 1)
                              .Concat(
                                from line in wrap1.Skip(1)
                                select new string(' ', maxprmLength + 1) + line
                              )
                        from wrappedLine in tmp
                        select wrappedLine
                    ).ToArray()
                )
            );
        }

        private static void DisplayExamples()
        {
            DisplayHeader();
            // Displays the error text wrapped to the console's width:
            Console.Error.WriteLine(
                String.Join(
                    Environment.NewLine,
                    (
                        from line in new string[] {
@"",
@"EXAMPLES:",
@"LinqFilter -q ""from line in lines select line""",
@"will echo all input lines delimited by Environment.NewLine",
@"",
@"LinqFilter -ln -q ""from li in lines select li.LineNumber.ToString() + \"" \"" + li.Text""",
@"will echo all input lines prefixed by their perceived input line numbers.",
@"",
@"LinqFilter -q ""from i in Enumerable.Range(1, 10) select i.ToString()"" will output the numbers 1 to 10 on the console delimited by Environment.NewLine and ignore input lines.",
                        }
                        // Wrap the lines to the window width:
                        from wrappedLine in line.WordWrap(consoleWidth - 1)
                        select wrappedLine
                    ).ToArray()
                )
            );
        }

        private static void DisplayExtensionsUsage()
        {
            DisplayHeader();
            // Displays the error text wrapped to the console's width:
            Console.Error.Write(
                String.Join(
                    Environment.NewLine,
                    (
                        from line in new string[] {
@"",
@"Static methods available for use in queries:",
@"  bool Warning(bool condition, Func<FormatArgs> formatMessage)",
@"  bool Error(bool condition, Func<FormatArgs> formatMessage)",
@"  IEnumerable<string> EnumerateLines(string path)",
@"  IEnumerable<string> EnumerateLines(TextReader reader)",
@"  IEnumerable<string> Single(string value)",
@"  string DecodeTabDelimited(string value)",
@"  string EncodeTabDelimited(string value)",
@"  string[] SplitTabDelimited(string line)",
@"  string JoinTabDelimited(params string[] cols)",
@"  string JoinTabDelimited(params object[] cols)",
@"  string JoinTabDelimited(IEnumerable<string> cols)",
@"  string JoinTabDelimited(IEnumerable<object> cols)",
@"  MaybeException<T> Try<T>(Func<T> throwableAction)",
@"  T If<T>(bool condition, Func<T> then, Func<T> @else)",
@"  bool Do(params Action[] actions)",
@"",
@"Extension methods:",
@"  U Case<T, U>(this T value, params CaseMatch<T, U>[] cases)",
@"  U Case<T, U>(this T value, Func<U> defaultCase, params CaseMatch<T, U>[] cases)",
@"  U Case<T, U>(this T value, IEqualityComparer<T> equalityComparer, params CaseMatch<T, U>[] cases)",
@"  U Case<T, U>(this T value, IEqualityComparer<T> equalityComparer, Func<U> defaultCase, params CaseMatch<T, U>[] cases)",
@"  TValue GetValueOr<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)",
@"  TValue GetValueOr<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> getDefaultValue)",
@"  T Mutate<T>(this T[] array, int index, T setValue)",
@"  T Mutate<T>(this IList<T> list, int index, T setValue)",
@"  T MutateAdd<T>(this IList<T> list, T setValue)",
@"  TValue Mutate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue setValue)",
@"  IEnumerable<string> WordWrap(this string text, int columns)",
@"  string NullTrim(this string value)",
@"  int ToInt32Or(this string value, int defaultValue)",
@"  int? ToInt32OrNull(this string value)",
@"",
@"Classes/structs exposed for use:",
@"  struct LineInfo {",
@"      int    LineNumber { get; }",
@"      string Text       { get; }",
@"  }",
@"  class FormatArgs {",
@"      FormatArgs(string format, params object[] args);",
@"      string   Format { get; }",
@"      object[] Args   { get; }",
@"  }",
@"  class MaybeException<T> {",
@"      T         Value        { get; }",
@"      Exception Exception    { get; }",
@"      bool      IsSuccessful { get; }",
@"  }",
@"",
@"CASE EXPRESSION:",
@"In WellDunne.Extensions.dll (automatically imported for you) is a useful extension method that gives you a way to do a `switch` statement in an expression, generally referred to in functional programming as a case expression.",
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
@"The CaseMatch arguments are evaluated in order. There is no check to ensure unique case match values. The default case is the first parameter and is executed if no cases match.",
                        }
                        // Wrap the lines to the window width:
                        from wrappedLine in line.WordWrap(consoleWidth - 1)
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
