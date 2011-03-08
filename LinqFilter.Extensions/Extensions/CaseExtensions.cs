using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    public static class CaseExtensions
    {
        public static TResult Case<TValue, TResult>(this TValue value, params CaseMatch<TValue, TResult>[] cases)
        {
            return Case(value, null, null, cases);
        }

        public static TResult Case<TValue, TResult>(this TValue value, Func<TResult> defaultCase, params CaseMatch<TValue, TResult>[] cases)
        {
            return Case(value, null, defaultCase, cases);
        }

        public static TResult Case<TValue, TResult>(this TValue value, IEqualityComparer<TValue> equalityComparer, params CaseMatch<TValue, TResult>[] cases)
        {
            return Case(value, equalityComparer, null, cases);
        }

        public static TResult Case<TValue, TResult>(this TValue value, IEqualityComparer<TValue> equalityComparer, Func<TResult> defaultCase, params CaseMatch<TValue, TResult>[] cases)
        {
            // FIXME: need to detect non-unique case values?
            if (equalityComparer == null)
                equalityComparer = EqualityComparer<TValue>.Default;

            // Run through the cases in order, using the equality comparer to find a match:
            foreach (CaseMatch<TValue, TResult> @case in cases)
            {
                if (equalityComparer.Equals(value, @case.TestValue))
                {
                    return @case.Expression();
                }
            }

            // Execute the default case's lambda:
            if (defaultCase == null)
                return default(TResult);

            return defaultCase();
        }
    }
}
