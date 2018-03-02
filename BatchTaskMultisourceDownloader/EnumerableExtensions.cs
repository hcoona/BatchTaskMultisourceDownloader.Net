using System;
using System.Collections.Generic;

namespace BatchTaskMultisourceDownloader
{
    internal static class EnumerableExtensions
    {
        // https://en.wikipedia.org/wiki/Reservoir_sampling#Algorithm_R
        public static IEnumerable<T> ReservoirSample<T>(this IEnumerable<T> source, Random random, int k)
        {
            var result = new List<T>(k);
            using (var e = source.GetEnumerator())
            {
                int i = 0;
                while (i < k && e.MoveNext())
                {
                    result.Add(e.Current);
                    i++;
                }

                if (i == k)
                {
                    while (e.MoveNext())
                    {
                        var j = random.Next(i);
                        if (j < k)
                        {
                            result[j] = e.Current;
                        }
                        i++;
                    }
                }
            }
            return result;
        }
    }
}
