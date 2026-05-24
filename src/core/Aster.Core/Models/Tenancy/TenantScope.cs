namespace Aster.Core.Models.Tenancy;

/// <summary>
/// Tenant boundary for tenant-aware SDK operations.
/// </summary>
public sealed record TenantScope
{
    /// <summary>
    /// Stable tenant identifier used by existing single-tenant callers.
    /// </summary>
    public const string DefaultTenantId = "default";

    /// <summary>
    /// The default single-tenant scope.
    /// </summary>
    public static TenantScope Default { get; } = new() { TenantId = DefaultTenantId };

    /// <summary>
    /// Opaque exact-match tenant identifier.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Whether this scope represents the default single-tenant environment.
    /// </summary>
    public bool IsDefault => string.Equals(TenantId, DefaultTenantId, StringComparison.Ordinal);

    /// <summary>
    /// Creates an explicit tenant scope.
    /// </summary>
    /// <param name="tenantId">Opaque exact-match tenant identifier.</param>
    /// <returns>A tenant scope for <paramref name="tenantId"/>.</returns>
    public static TenantScope FromTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new TenantScope { TenantId = tenantId };
    }

    /// <summary>
    /// Resolves an optional scope to an effective scope.
    /// </summary>
    /// <param name="scope">Optional tenant scope.</param>
    /// <returns><paramref name="scope"/> when supplied; otherwise <see cref="Default"/>.</returns>
    public static TenantScope Resolve(TenantScope? scope) => scope ?? Default;
}
