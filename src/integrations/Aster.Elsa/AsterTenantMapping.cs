using Aster.Core.Exceptions;
using Aster.Core.Models.Tenancy;
using ElsaTenant = Elsa.Common.Multitenancy.Tenant;
using ITenantAccessor = Elsa.Common.Multitenancy.ITenantAccessor;

namespace Aster.Elsa;

/// <summary>
/// Maps Elsa tenant IDs onto Aster tenant scopes:
/// <list type="bullet">
/// <item><c>""</c> (Elsa's default tenant) is <see cref="TenantScope.Default"/>;</item>
/// <item>any other ID is <see cref="TenantScope.FromTenantId"/> with the same ID;</item>
/// <item>the agnostic <c>*</c> and an Elsa tenant literally named <c>default</c> are refused with
/// <see cref="TenantScopeException"/> (<c>tenant-scope-invalid</c>), because the first has no Aster equivalent and the
/// second would share Aster's default scope with Elsa's default tenant.</item>
/// </list>
/// </summary>
public static class AsterTenantMapping
{
    /// <summary>Gets the Aster tenant scope for an Elsa tenant ID.</summary>
    /// <param name="elsaTenantId">The Elsa tenant ID, as <see cref="ITenantAccessor.TenantId"/> reports it; <see langword="null"/> is the default tenant.</param>
    /// <returns>The Aster tenant scope.</returns>
    /// <exception cref="TenantScopeException">The ID is <c>*</c> or <c>default</c>.</exception>
    public static TenantScope FromElsaTenantId(string? elsaTenantId)
    {
        if (string.IsNullOrEmpty(elsaTenantId))
            return TenantScope.Default;

        if (string.Equals(elsaTenantId, ElsaTenant.AgnosticTenantId, StringComparison.Ordinal))
            throw new TenantScopeException(
                TenantScopeException.InvalidCode,
                $"The agnostic Elsa tenant '{ElsaTenant.AgnosticTenantId}' has no Aster tenant scope. Run Aster activities in a specific tenant.",
                elsaTenantId);

        if (string.Equals(elsaTenantId, TenantScope.DefaultTenantId, StringComparison.Ordinal))
            throw new TenantScopeException(
                TenantScopeException.InvalidCode,
                $"The Elsa tenant '{TenantScope.DefaultTenantId}' would share Aster's default tenant scope with Elsa's default tenant (''). Rename the Elsa tenant.",
                elsaTenantId);

        if (string.IsNullOrWhiteSpace(elsaTenantId))
            throw new TenantScopeException(
                TenantScopeException.InvalidCode,
                "An Elsa tenant ID of only whitespace has no Aster tenant scope.",
                elsaTenantId);

        return TenantScope.FromTenantId(elsaTenantId);
    }

    /// <summary>
    /// Gets the Aster tenant scope for the current Elsa tenant, read from <paramref name="tenantAccessor"/> at the time
    /// of the call. Without an accessor (Elsa's multitenancy services are not registered) it is the default scope.
    /// </summary>
    /// <param name="tenantAccessor">Elsa's tenant accessor, if registered.</param>
    /// <returns>The Aster tenant scope.</returns>
    /// <exception cref="TenantScopeException">The current Elsa tenant ID is <c>*</c> or <c>default</c>.</exception>
    public static TenantScope FromTenantAccessor(ITenantAccessor? tenantAccessor) => FromElsaTenantId(tenantAccessor?.TenantId);
}
