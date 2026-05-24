using Aster.Core.Exceptions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Resolves and validates tenant scopes at operation boundaries.
/// </summary>
public static class TenantScopeResolver
{
    /// <summary>
    /// Resolves an optional tenant scope to an effective tenant scope.
    /// </summary>
    /// <param name="scope">Optional tenant scope.</param>
    /// <returns>The effective tenant scope.</returns>
    /// <exception cref="TenantScopeException">Thrown when the supplied scope is invalid.</exception>
    public static TenantScope Resolve(TenantScope? scope)
    {
        var effective = TenantScope.Resolve(scope);
        if (string.IsNullOrWhiteSpace(effective.TenantId))
        {
            throw new TenantScopeException(
                TenantScopeException.InvalidCode,
                "Tenant scope must include a non-empty tenant identifier.");
        }

        return effective;
    }

    /// <summary>
    /// Resolves an optional tenant scope to its effective tenant identifier.
    /// </summary>
    /// <param name="scope">Optional tenant scope.</param>
    /// <returns>The effective tenant identifier.</returns>
    public static string ResolveTenantId(TenantScope? scope) => Resolve(scope).TenantId;
}
