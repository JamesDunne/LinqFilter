using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.CodeDom.Compiler;
using System.Text;

namespace LinqFilter
{
    class Program
    {
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

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine(
@"LinqFilter.exe [<line of LINQ code> | -i [filename] ] ...

Each non-option argument is appended line-by-line to form a LINQ query
expression. This query is run over the stdin lines of input to produce
stdout lines of output.

-i [filename]  is used to import a section of lines of LINQ query expression
               code from a file. This option can be repeated as many times in
               order to compose larger queries from files containing partial
               bits of code.
-u [namespace] is used to add a `using namespace;` line.
-r [assembly]  is used to add a reference to a required assembly.

The final constructed query must be of the form:
   from <range variable> in lines
   ...
   select <string variable>

The resulting type of the query must be IEnumerable<string>. The source
is an `IEnumerable<string>` named `lines`.");
                Environment.ExitCode = -2;
                return;
            }

            // Start off a new StringBuilder with a reasonable expected capacity:
            StringBuilder sbLinq = new StringBuilder(args.Sum(a => a.Length));

            HashSet<string> usingNamespaces = new HashSet<string>(new string[] {
                "System",
                "System.Collections.Generic",
                "System.Linq"
            });

            HashSet<string> reffedAssemblies = new HashSet<string>(new string[] {
                "mscorlib.dll",
                "System.dll",
                "System.Core.dll"
            }, StringComparer.InvariantCultureIgnoreCase);

            // Process cmdline arguments:
            Queue<string> argQueue = new Queue<string>(args);
            while (argQueue.Count > 0)
            {
                string arg = argQueue.Dequeue();
                if (arg.StartsWith("-"))
                {
                    if (arg == "-i")
                    {
                        if (!AssertMoreArguments(argQueue, "-i option expects a filename")) return;

                        // Pop the argument and load the file:
                        string path = argQueue.Dequeue();
                        string tmpCode = File.ReadAllText(path);
                        // Append the loaded file contents to the StringBuilder
                        sbLinq.AppendLine(tmpCode);
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
                    else
                    {
                        Console.Error.WriteLine("Unrecognized option \"{0}\"!", arg);
                        Environment.ExitCode = -2;
                        return;
                    }
                }
                else
                {
                    // Append the argument as a line of code in the LINQ query:
                    sbLinq.AppendLine(arg);
                }
            }

            // Create an IEnumerable<string> that reads stdin line by line:
            IEnumerable<string> lines = StreamLines(Console.In);

            // Create a C# v3.5 compiler provider:
            var provider = new Microsoft.CSharp.CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v3.5" } });

            // Add some basic assembly references:
            var options = new CompilerParameters();
            options.ReferencedAssemblies.AddRange(reffedAssemblies.ToArray());

            // NOTE: Yes, I realize this is easily injectible. You should only be running this on your local machine
            // and so you implicitly trust yourself to do nothing nefarious to yourself. If you're trying to subvert
            // the mechanisms of the static method, then you've missed the point of this tool.
            var results = provider.CompileAssemblyFromSource(
                options,
                new string[] {
                    // Add the using namespace lines:
                    String.Join(Environment.NewLine, (
                        from ns in usingNamespaces
                        orderby ns
                        select "using " + ns + ";"
                    ).ToArray()) +
@"
public class DynamicQuery
{
    public static IEnumerable<string> GetQuery(IEnumerable<string> lines)
    {
        var query = " + sbLinq.ToString() + @";
        return query;
    }
}"
                }
            );

            // Check compilation errors:
            if (results.Errors.Count > 0)
            {
                foreach (var error in results.Errors)
                {
                    Console.Error.WriteLine(error);
                }
                Environment.ExitCode = -1;
                return;
            }

            // Find the compiled assembly's DynamicQuery type and execute its static GetQuery method:
            var t = results.CompiledAssembly.GetType("DynamicQuery");
            IEnumerable<string> lineQuery = (IEnumerable<string>)t.GetMethod("GetQuery").Invoke(null, new object[1] { lines });

            // Run the filter:
            foreach (string line in lineQuery)
            {
                Console.Out.WriteLine(line);
            }
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
