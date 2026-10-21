using System.Runtime.CompilerServices;
using Aster.Core.Exceptions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Input rules for the identifiers a new record carries: resource IDs, definition IDs, aspect definition IDs,
/// activation channels and tenant IDs. Core and the built-in providers apply them where a record is created,
/// registered, activated or imported, so every provider rejects the same input with the same code.
/// </summary>
/// <remarks>
/// Arguments that only look up an existing record (reads, updates, deactivation, version history, lifecycle markers,
/// policy application and query filters) are not checked, so records stored before the rule existed stay reachable.
/// </remarks>
public static class IdentifierValidator
{
    /// <summary>
    /// Returns whether <paramref name="value"/> starts or ends with a whitespace character.
    /// </summary>
    /// <param name="value">The identifier to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the first or last character is whitespace (<see cref="char.IsWhiteSpace(char)"/>);
    /// <see langword="false"/> for <see langword="null"/> or an empty string.
    /// </returns>
    public static bool HasSurroundingWhitespace(string? value) =>
        !string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));

    /// <summary>
    /// Throws when <paramref name="value"/> starts or ends with whitespace. <see langword="null"/> and empty values
    /// pass; reject those separately where they are not allowed.
    /// </summary>
    /// <param name="value">The identifier to check.</param>
    /// <param name="paramName">The parameter or property that carries the identifier.</param>
    /// <exception cref="InvalidIdentifierException">
    /// <paramref name="value"/> starts or ends with whitespace. Its code is
    /// <see cref="InvalidIdentifierException.SurroundingWhitespaceCode"/>.
    /// </exception>
    public static void ThrowIfSurroundingWhitespace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!HasSurroundingWhitespace(value))
            return;

        throw new InvalidIdentifierException(
            InvalidIdentifierException.SurroundingWhitespaceCode,
            $"Identifier '{value}' must not start or end with whitespace.",
            paramName,
            value);
    }

    /// <summary>
    /// Checks the identifiers a resource creation writes: the definition ID, the caller-supplied resource ID and the
    /// tenant ID. A blank resource ID means "generate one", so it is not checked.
    /// </summary>
    internal static void ThrowIfCreateIdentifiersInvalid(string definitionId, string? resourceId, TenantScope tenant)
    {
        ThrowIfSurroundingWhitespace(definitionId);
        if (!string.IsNullOrWhiteSpace(resourceId))
            ThrowIfSurroundingWhitespace(resourceId);
        ThrowIfSurroundingWhitespace(tenant.TenantId);
    }
}
