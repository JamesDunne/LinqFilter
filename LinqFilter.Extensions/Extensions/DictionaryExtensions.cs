using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Collections.Generic
{
    public static class DictionaryExtensions
    {
        public static TValue GetValueOr<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        {
            if (dict == null) return defaultValue;

            TValue value = defaultValue;
            dict.TryGetValue(key, out value);
            return value;
        }

        public static TValue GetValueOr<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> getDefaultValue)
        {
            if (dict == null) return getDefaultValue();

            TValue value;
            if (dict.TryGetValue(key, out value))
            {
                return value;
            }

            return getDefaultValue();
        }
    }
}
