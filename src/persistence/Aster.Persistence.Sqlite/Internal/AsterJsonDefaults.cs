using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aster.Persistence.Sqlite.Internal;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for serialising and deserialising
/// persisted JSON columns (<c>PayloadJson</c>, <c>AspectsJson</c>, <c>ActiveVersionsJson</c>).
/// </summary>
internal static class AsterJsonDefaults
{
    /// <summary>
    /// Standard options for all persistence JSON. Uses camelCase property names
    /// and includes enum string conversion for readability.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
