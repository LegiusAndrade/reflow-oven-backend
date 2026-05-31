using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ReflowOven.Domain.Common;

/// <summary>
/// Maps enum values to/from the exact pt-BR wire string declared with
/// <see cref="JsonStringEnumMemberNameAttribute"/> (falling back to the member name). Used by the
/// EF value converter so DB text and JSON share one mapping, and by query-string filters that arrive
/// as the wire literal (model binding would otherwise match the member name, not the attribute).
/// Reflection is cached.
/// </summary>
public static class EnumWire
{
    private static readonly ConcurrentDictionary<Type, (Dictionary<object, string> toWire, Dictionary<string, object> fromWire)> Cache = new();

    private static (Dictionary<object, string> toWire, Dictionary<string, object> fromWire) MapsFor(Type enumType) =>
        Cache.GetOrAdd(enumType, t =>
        {
            var to = new Dictionary<object, string>();
            var from = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = f.GetValue(null)!;
                var wire = f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? f.Name;
                to[value] = wire;
                from[wire] = value;
            }
            return (to, from);
        });

    public static string ToWire<TEnum>(TEnum value) where TEnum : struct, Enum =>
        MapsFor(typeof(TEnum)).toWire[value];

    public static TEnum FromWire<TEnum>(string wire) where TEnum : struct, Enum =>
        MapsFor(typeof(TEnum)).fromWire.TryGetValue(wire, out var v) ? (TEnum)v : Enum.Parse<TEnum>(wire);

    /// <summary>Tolerant parse of a pt-BR wire literal; returns false (no throw) for an unknown value.</summary>
    public static bool TryFromWire<TEnum>(string? wire, out TEnum value) where TEnum : struct, Enum
    {
        if (wire is not null && MapsFor(typeof(TEnum)).fromWire.TryGetValue(wire, out var v))
        {
            value = (TEnum)v;
            return true;
        }
        value = default;
        return false;
    }
}
