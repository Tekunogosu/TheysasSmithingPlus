#nullable enable
using System;
using System.Collections.Generic;

namespace SmithingPlus.Util;

public static class CacheHelper
{
    /// <summary>
    ///     Gets a value from the cache, computing and storing it on a miss.
    ///     <para>
    ///         A null from <paramref name="valueFactory" /> is returned without being stored, so the next
    ///         call recomputes it. Use this only where computing null is cheap, or where null means "not
    ///         ready yet" and a later call is expected to produce a real value. Where null is a legitimate,
    ///         expensive-to-determine answer, use <see cref="GetOrAddNullable{TKey,TValue}" /> instead.
    ///     </para>
    /// </summary>
    /// <param name="cache"> The cache to get or add the value from. </param>
    /// <param name="key"> The key to get or add the value with. </param>
    /// <param name="valueFactory"> The factory to create the value if it doesn't exist. </param>
    /// <param name="onAdd"> The action to perform when the value is added. </param>
    /// <typeparam name="TKey"> The type of the key. </typeparam>
    /// <typeparam name="TValue"> The type of the value. </typeparam>
    public static TValue? GetOrAdd<TKey, TValue>(
        IDictionary<TKey, TValue> cache,
        TKey key,
        Func<TValue> valueFactory,
        Action<TKey, TValue>? onAdd = null)
    {
        if (cache.TryGetValue(key, out var value)) return value;
        value = valueFactory();
        if (value == null) return default;
        cache[key] = value;
        onAdd?.Invoke(key, value);
        return value;
    }

    /// <summary>
    ///     Gets a value from the cache, computing and storing it on a miss, storing null as a result in its
    ///     own right.
    ///     <para>
    ///         A cache that drops nulls does not memoise the case it most needs to: "this has no answer" is
    ///         reached only after every fallback has been tried and failed, which is the most expensive path
    ///         through the lookup, and it is recomputed on every call. Resolving the metal of a collectible
    ///         that has none ends in a search over the recipe list; with nulls dropped, every anvil-workable
    ///         item without a metal pays for that search again on each look at an anvil.
    ///     </para>
    ///     <para>
    ///         The distinction from <see cref="GetOrAdd{TKey,TValue}" /> is whether null means "no answer"
    ///         or "no answer yet": a stored null is never revisited, so callers whose null is a transient
    ///         not-loaded-yet state must not use this.
    ///     </para>
    /// </summary>
    public static TValue? GetOrAddNullable<TKey, TValue>(
        IDictionary<TKey, TValue?> cache,
        TKey key,
        Func<TValue?> valueFactory,
        Action<TKey, TValue?>? onAdd = null)
        where TValue : class
    {
        if (cache.TryGetValue(key, out var value)) return value;
        value = valueFactory();
        cache[key] = value;
        onAdd?.Invoke(key, value);
        return value;
    }
}
