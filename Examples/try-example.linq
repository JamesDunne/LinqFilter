from line in lines
// use Try(Func<T>) to try/catch in a functional manner
let maybeInt = Try(() => Int32.Parse(line))
// Try returns a MaybeException<T> which has an IsSuccessful bool param that lets you
// know whether or not the function threw an exception or returned a value.
let success = maybeInt.IsSuccessful
// Use the Warning() method to check the bool condition param. If false, the lambda is
// invoked to return a FormatArgs class which is constructed with (string format,
// params object[] args) and that is passed to String.Format and written to Console.Error.
where Warning(success, () => new FormatArgs( maybeInt.Exception.ToString() ))
// You can either use the implicit conversion operator or the Value property to
// retrieve the value. Conversely, use the Exception property to retrieve the
// exception.
select ((int)maybeInt).ToString()