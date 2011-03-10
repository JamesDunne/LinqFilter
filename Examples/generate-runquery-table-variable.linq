// Generates a partial SQL script to declare a table variable and INSERT statements to insert data
// from Console.In into the table variable.
//-pre

string nameRow = lines.First();
string dataTypeRow = lines.First();
string[] names = SplitTabDelimited(nameRow);
string[] dataTypeNames = SplitTabDelimited(dataTypeRow);
if (names.Length != dataTypeNames.Length) throw new Exception("Name row and DataType row lengths don't match!");

// Enumerate all the Console.In lines and convert them to rows of columns:
var rows = (
    // Skip the header row:
    from line in lines
    where line.Length > 0
    let cols = SplitTabDelimited(line)
    where Warning(cols.Length == names.Length, "Must have {0} columns per row!", names.Length)
    select cols
).ToList();

// Nothing to do:
if (rows.Count == 0) return Enumerable.Empty<string>();

var nameList = String.Join(",", names.Select(n => "[" + n + "]").ToArray());

// You can declare inline functions with lambda syntax in the -pre section:
Func<int, string> getSQLDataTypeName = (ordinal) => dataTypeNames[ordinal].Case(
    () => dataTypeNames[ordinal],
    Match.Case( "varchar", () =>  "varchar(" + rows.Max(row => row[ordinal].Length).ToString() + ")"),
    Match.Case("nvarchar", () => "nvarchar(" + rows.Max(row => row[ordinal].Length).ToString() + ")")
);
Func<string[], int, string> formatSQLValue = (row, ordinal) => row[ordinal].Case(
    // default case is now dependent on data type:
    () => dataTypeNames[ordinal].Case(
        () => row[ordinal],
        Match.Case( "varchar", () =>  "'" + row[ordinal].Replace("'", "''") + "'"),
        Match.Case("nvarchar", () => "N'" + row[ordinal].Replace("'", "''") + "'"),
        Match.Case("datetime", () =>  "'" + row[ordinal].Replace("'", "''") + "'")
    ),
    Match.Case("\0", () => "NULL")  // display \0 as NULL
);

//-q
new string[] {
    "DECLARE @data TABLE (",
}.Concat(
    // Declare each column in SQL syntax:
    from i in Enumerable.Range(0, names.Length)
    select String.Concat(
        "  [", names[i], "] ", getSQLDataTypeName(i),
        // determine if NULLs are present:
        (rows.Any(row => row[i] == "\0") ? String.Empty : " NOT NULL"),
        ((i < names.Length - 1) ? "," : String.Empty)
    )
).Concat(new string[] {
    ");",
    ""
}).Concat(
    from row in rows
    let valuesList = String.Join(
        ",",
        (from i in Enumerable.Range(0, names.Length)
         select formatSQLValue(row, i)).ToArray()
    )
    select @"INSERT INTO @data (" + nameList + @") VALUES (" + valuesList + @");"
)
