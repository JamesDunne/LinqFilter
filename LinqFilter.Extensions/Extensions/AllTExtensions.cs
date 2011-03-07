using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    public static class AllTExtensions
    {
        /// <summary>
        /// A poor-man's case expression using default equality comparer semantics.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="equalityComparer"></param>
        /// <param name="cases"></param>
        /// <returns></returns>
        public static TResult Case<TValue, TResult>(this TValue value, params CaseMatch<TValue, TResult>[] cases)
        {
            return Case(value, null, cases);
        }

        /// <summary>
        /// A poor man's case expression for C#.
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="value"></param>
        /// <param name="equalityComparer"></param>
        /// <param name="cases"></param>
        /// <returns></returns>
        public static TResult Case<TValue, TResult>(this TValue value, IEqualityComparer<TValue> equalityComparer, params CaseMatch<TValue, TResult>[] cases)
        {
#if true
            // FIXME: need to detect non-unique case values?
            CaseMatch<TValue, TResult> defaultCase = null;
            if (equalityComparer == null) equalityComparer = EqualityComparer<TValue>.Default;

            foreach (CaseMatch<TValue, TResult> @case in cases)
            {
                if (@case.IsDefault)
                {
                    defaultCase = @case;
                }
                else if (equalityComparer.Equals(value, @case.TestValue))
                {
                    return @case.Expression();
                }
            }

            if (defaultCase == null) defaultCase = CaseMatch<TValue, TResult>.Default;
            return defaultCase.Expression();
#else
            // NOTE: This is VERY slow compared to the above implementation:

            CaseMatch<T> defaultCase;

            // Must have only one default case:
            List<CaseMatch<T>> defaultCases = cases.Where(c => c.IsDefault).ToList();

            if (defaultCases.Count > 1)
                throw new ArgumentException("Multiple default cases found. Use CaseMatch ctor with one parameter to specify a single default case.", "cases");

            // If no default case, ensure a default implementation:
            if (defaultCases.Count == 0)
            {
                //throw new ArgumentException("No default case found. Use CaseMatch ctor with one parameter to specify a single default case.", "cases");
                defaultCase = CaseMatch<T>.Default;
            }
            else
            {
                defaultCase = defaultCases[0];
            }

            CaseMatch<T>[] nonDefaultCases = cases.Where(c => !c.IsDefault).ToArray();

            // Turn the cases into a dictionary, keyed on the `TestValue`s according to the given comparer:
            Dictionary<T, CaseMatch<T>> testValueSet;
            if (equalityComparer != null)
            {
                testValueSet = nonDefaultCases.ToDictionary(c => c.TestValue, equalityComparer);
            }
            else
            {
                testValueSet = nonDefaultCases.ToDictionary(c => c.TestValue);
            }

            // Is the set's count less than the number of cases? That indicates that TestValues were duplicates according to the `equalityComparer`.
            if (testValueSet.Count < nonDefaultCases.Length)
                throw new ArgumentException("Test values are not unique according to `equalityComparer`.", "cases");

            // Do the dictionary lookup to find the match:
            CaseMatch<T> match;
            if (testValueSet.TryGetValue(value, out match))
                return match.Expression();

            // Return the default case:
            return defaultCase.Expression();
#endif
        }
    }
}
