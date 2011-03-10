// Generates a partial SQL script to declare a table variable and INSERT statements to insert data
// from Console.In into the table variable.
//-pre

// Enumerate all the Console.In lines and convert them to rows of anonymous objects:
var rows = (
    // Skip the header row:
    from line in lines.Skip(1)
    where line.Length > 0
    let cols = line.Split('\t')
    where Warning(cols.Length >= 3, "Must have at least 3 columns per row!")
    // This anonymous type defines how the INSERT statement gets generated:
    let row = new {
        Column1 = cols[0].Trim(),
        Column2 = Int32.Parse(cols[1].Trim()),
        Column3 = DateTime.Parse(cols[2].Trim()),
    }
    select row
).ToList();

if (rows.Count == 0) return Enumerable.Empty<string>();

var props = rows[0].GetType().GetProperties();
var propGetMethods = props.Select(prop => prop.GetGetMethod()).ToArray();
var propNameList = String.Join(",", props.Select(prop => prop.Name).ToArray());

// You can declare inline functions with lambda syntax in the -pre section:
Func<int, string> getSQLDataTypeName = (ordinal) => props[ordinal].PropertyType.FullName.Case(
    () => props[ordinal].PropertyType.Name,
    Match.Case("System.Int32",    () => "int"),
    Match.Case("System.String",   () => "nvarchar(" + rows.Max(row => ((string)propGetMethods[ordinal].Invoke(row, null)).Length).ToString() + ")"),
    Match.Case("System.DateTime", () => "datetime"),
    Match.Case("System.Boolean",  () => "bit")
    // TODO: continue mapping up CLR types to SQL type names...
);

//-q
new string[] {
    "DECLARE @data TABLE (",
}.Concat(
    // Declare each column in SQL syntax:
    props.Select((prop,i) => String.Concat(prop.Name, " ", getSQLDataTypeName(i)) + ((i < props.Length - 1) ? "," : String.Empty))
).Concat(new string[] {
    ");",
    ""
}).Concat(
    from row in rows
    let propValuesList = String.Join(
        ",",
        (from prop in props
         let value = prop.GetGetMethod().Invoke(row, null)
         let sqlValue = prop.PropertyType.Case(
            () => value.ToString(),
            // case typeof(string):
            Match.Case(typeof(string), () => "'" + ((string)value).Replace("'", "''") + "'")
         )
         select sqlValue).ToArray()
    )
    select @"INSERT INTO @data (" + propNameList + ") VALUES (" + propValuesList + ");"
)
