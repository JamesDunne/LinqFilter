using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    /// <summary>
    /// A small class which holds basic metadata describing a case expression's single case match.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class CaseMatch<TValue, TResult>
    {
        /// <summary>
        /// Gets a boolean value that indicates whether or not this case is the default case.
        /// </summary>
        public bool IsDefault { get; private set; }

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
        public CaseMatch(TValue testValue, Func<TResult> expression)
        {
            TestValue = testValue;
            Expression = expression;
            IsDefault = false;
        }

        /// <summary>
        /// Construct a default case match.
        /// </summary>
        /// <param name="expression">Lambda to invoke to return value</param>
        public CaseMatch(Func<TResult> expression)
        {
            Expression = expression;
            IsDefault = true;
        }

        private static readonly CaseMatch<TValue, TResult> _default = new CaseMatch<TValue, TResult>(() => default(TResult));

        /// <summary>
        /// Gets a default case match that returns default(<typeparamref name="TResult"/>).
        /// </summary>
        public static CaseMatch<TValue, TResult> Default { get { return _default; } }
    }
}
