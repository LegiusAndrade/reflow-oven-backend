using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ReflowOven.Infrastructure.Persistence.Conversions;

/// <summary>Stores an enum as its pt-BR wire string in PostgreSQL (see <see cref="EnumWire"/>).</summary>
public sealed class PtBrEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => EnumWire.ToWire(v),
    s => EnumWire.FromWire<TEnum>(s))
    where TEnum : struct, Enum;
