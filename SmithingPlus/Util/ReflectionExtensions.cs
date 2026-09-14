#nullable enable
using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace SmithingPlus.Util;

/// <summary>
///     Reads and writes private fields on objects owned by the game or by other mods, where there is no
///     public accessor to use instead.
///     <para>
///         Resolved <see cref="FieldInfo" />s are cached per type and field name. A lookup by name walks the
///         type's field table on every call, and these run on paths that repeat per item, per frame, or per
///         anvil-workable collectible in the game, where the same handful of fields are read over and over.
///     </para>
/// </summary>
public static class ReflectionExtensions
{
    private const BindingFlags Own = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Inherited = Own | BindingFlags.FlattenHierarchy;

    private static readonly ConcurrentDictionary<(Type Type, string Field, BindingFlags Flags), FieldInfo?> Fields =
        new();

    /// <summary>The named private field declared on the object's own type, or null if there is none.</summary>
    public static T? GetField<T>(this object obj, string fieldName)
    {
        return obj.ReadField<T>(fieldName, Own);
    }

    /// <summary>The named private field, including ones declared by base types.</summary>
    public static T? GetInternalField<T>(this object obj, string fieldName)
    {
        return obj.ReadField<T>(fieldName, Inherited);
    }

    public static void SetField<T>(this object obj, string fieldName, T newValue)
    {
        obj.WriteField(fieldName, newValue, Own);
    }

    public static void SetInternalField<T>(this object obj, string fieldName, T newValue)
    {
        obj.WriteField(fieldName, newValue, Inherited);
    }

    private static T? ReadField<T>(this object obj, string fieldName, BindingFlags flags)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var field = Field(obj.GetType(), fieldName, flags);
        return field?.GetValue(obj) is T value ? value : default;
    }

    private static void WriteField<T>(this object obj, string fieldName, T newValue, BindingFlags flags)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var field = Field(obj.GetType(), fieldName, flags)
                    ?? throw new InvalidOperationException(
                        $"Field '{fieldName}' not found on {obj.GetType().FullName}.");
        field.SetValue(obj, newValue);
    }

    private static FieldInfo? Field(Type type, string fieldName, BindingFlags flags)
    {
        return Fields.GetOrAdd((type, fieldName, flags), key => key.Type.GetField(key.Field, key.Flags));
    }
}
