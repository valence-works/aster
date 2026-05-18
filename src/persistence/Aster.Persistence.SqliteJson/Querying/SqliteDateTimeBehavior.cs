using Aster.Core.Models.Querying;

namespace Aster.Persistence.SqliteJson.Querying;

internal static class SqliteDateTimeBehavior
{
    public const string DateKeyFunction = "aster_datetime_key";

    public static string? NormalizeDateKey(string? value) =>
        QueryDateTimeValue.TryNormalizeDateKey(value, out var key) ? key : null;

    public static bool TryNormalizeDateKey(object? value, out string key) =>
        QueryDateTimeValue.TryNormalizeDateKey(value, out key);
}
