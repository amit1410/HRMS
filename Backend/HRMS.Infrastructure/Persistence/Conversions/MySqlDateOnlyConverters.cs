using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRMS.Infrastructure.Persistence.Conversions;

/// <summary>Converts MySql.Data's DateTime result for SQL date columns to the domain DateOnly type.</summary>
internal sealed class MySqlDateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public MySqlDateOnlyConverter()
        : base(
            value => value.ToDateTime(TimeOnly.MinValue),
            value => DateOnly.FromDateTime(value))
    {
    }
}

/// <summary>Nullable counterpart for MySQL SQL date columns.</summary>
internal sealed class MySqlNullableDateOnlyConverter : ValueConverter<DateOnly?, DateTime?>
{
    public MySqlNullableDateOnlyConverter()
        : base(
            value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
            value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null)
    {
    }
}
