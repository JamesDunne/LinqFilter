using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System
{
    public static class MutationExtensions
    {
        public static T Mutate<T>(this T[] array, int index, T setValue)
        {
            array[index] = setValue;
            return setValue;
        }

        public static T Mutate<T>(this IList<T> list, int index, T setValue)
        {
            list[index] = setValue;
            return setValue;
        }

        public static T MutateAdd<T>(this IList<T> list, T setValue)
        {
            list.Add(setValue);
            return setValue;
        }

        public static TValue Mutate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue setValue)
        {
            dict[key] = setValue;
            return setValue;
        }
    }
}
