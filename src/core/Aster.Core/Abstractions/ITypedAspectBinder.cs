namespace Aster.Core.Abstractions;

/// <summary>
/// Serializes and deserializes a full aspect POCO to and from raw storage format.
/// Uses State Replace semantics: the entire aspect value is replaced on write.
/// </summary>
public interface ITypedAspectBinder
{
    /// <summary>
    /// Serializes a POCO value to the raw aspect storage format.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the full aspect.</typeparam>
    /// <param name="value">The aspect value to serialize.</param>
    /// <returns>The raw storage representation.</returns>
    object? Serialize<T>(T value);

    /// <summary>
    /// Deserializes a raw aspect storage value to the specified POCO type.
    /// </summary>
    /// <typeparam name="T">The target POCO type.</typeparam>
    /// <param name="raw">The raw value from storage.</param>
    /// <returns>The deserialized POCO, or <see langword="default"/> if <paramref name="raw"/> is <see langword="null"/>.</returns>
    T? Deserialize<T>(object? raw);
}
