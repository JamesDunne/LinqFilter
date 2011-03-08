using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    /// <summary>
    /// Helper static class for shortening the syntax for creating case/default expressions.
    /// </summary>
    public static class Match
    {
        /// <summary>
        /// Creates a match case.
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="testValue">The value to test against.</param>
        /// <param name="expression">The lambda expression to invoke to yield a value if this case matches.</param>
        /// <returns></returns>
        public static CaseMatch<TValue, TResult> Case<TValue, TResult>(TValue testValue, Func<TResult> expression)
        {
            if (expression == null) throw new ArgumentNullException("expression");

            return new CaseMatch<TValue, TResult>(testValue, expression);
        }
    }

    /// <summary>
    /// A small class which holds basic metadata describing a case expression's single case match.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class CaseMatch<TValue, TResult>
    {
        /// <summary>
        /// Gets the test value used for comparison.
        /// </summary>
        public TValue TestValue { get; private set; }

        /// <summary>
        /// Gets the lambda that yields the return value of the case expression.
        /// </summary>
        public Func<TResult> Expression { get; private set; }

        /// <summary>
        /// Construct a specific case match.
        /// </summary>
        /// <param name="testValue">Value to test against</param>
        /// <param name="expression">Lambda to invoke to return value</param>
        internal CaseMatch(TValue testValue, Func<TResult> expression)
        {
            if (expression == null) throw new ArgumentNullException("expression");

            TestValue = testValue;
            Expression = expression;
        }
    }
}
