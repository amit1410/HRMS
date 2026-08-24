using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRMS.Infrastructure.Persistence.Conversions;

/// <summary>
/// Keeps every DateTime in the model an explicit UTC instant on both sides of the database.
/// <para>
/// Reading is the part that matters. Providers hand back <see cref="DateTimeKind.Unspecified"/> — SQL Server
/// (<c>datetime2</c>) and the SQLite dev fallback (TEXT) both do — so a timestamp fetched from a table
/// serialized as "2026-08-22T03:22:24" while the very same timestamp still in memory serialized as
/// "2026-08-22T03:22:24Z". A client parsing the first form treats it as local time and shifts the value by
/// its own offset, which shows up as audit dates that disagree with each other by a few hours.
/// </para>
/// <para>
/// Writing only normalizes a Local value, which has a real offset to convert. Unspecified is stored as
/// given: it is already what the caller meant by "this instant", and inventing a zone for it would move
/// seeded and imported dates.
/// </para>
/// </summary>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
