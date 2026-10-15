#if NETFRAMEWORK
using System;
using System.Collections.Generic;

// The audit rules shared with the Plant 3D plug-in are written for .NET 8;
// Navisworks hosts .NET Framework 4.8. These fill the few members they use
// that the older framework lacks (an instance member, where it exists,
// always wins over an extension, so nothing changes on .NET 8).
namespace System.Text.Json.Serialization
{
    // AuditIssue marks a property as not serialized; the Navisworks build
    // writes its JSON itself (NavisJsonReport) and has no System.Text.Json.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    internal sealed class JsonIgnoreAttribute : Attribute { }
}

// In the shared code's root namespace, so PipingSpecPlugin.Audit / .Spec
// see the extensions without a using.
namespace PipingSpecPlugin
{
    internal static class FrameworkPolyfills
    {
        public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second) =>
            System.Linq.Enumerable.Zip(first, second, (a, b) => (a, b));

        public static bool Contains(this string text, string value, StringComparison comparison) =>
            text.IndexOf(value, comparison) >= 0;

        public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;

        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
            dictionary.TryGetValue(key, out var value) ? value : default;
    }
}
#endif
