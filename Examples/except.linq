//-lf
//-u System.IO
//-pre
if (args.Length == 0) throw new Exception("Required -a <path>");
var excepts = new HashSet<string>( File.ReadAllLines(args[0]) );
//-q
from line in lines
where !excepts.Contains(line)
select line