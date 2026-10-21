using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Aster.AspNetCore;

/// <summary>
/// Resolves the tenant of a request. Register an implementation to scope every Aster endpoint to a tenant taken
/// from the host's authentication, route or headers; without one, every request uses <see cref="TenantScope.Default"/>.
/// Never trust a tenant the client names without checking it against the caller's identity.
/// </summary>
public interface ITenantScopeAccessor
{
    /// <summary>Returns the tenant the request operates in.</summary>
    /// <param name="httpContext">The current request.</param>
    TenantScope GetTenantScope(HttpContext httpContext);
}

/// <summary>
/// The operation a request performs on a resource, for <see cref="IAsterResourceAuthorizer"/>.
/// </summary>
public enum AsterResourceOperation
{
    /// <summary>Reading a resource: list, query, detail and version endpoints.</summary>
    Read,

    /// <summary>Creating a resource of a definition.</summary>
    Create,

    /// <summary>Saving a new version of a resource.</summary>
    Update,

    /// <summary>Activating or deactivating a version in a channel.</summary>
    Activate,
}

/// <summary>
/// The resource a request reads or changes, for <see cref="IAsterResourceAuthorizer"/>.
/// </summary>
/// <param name="HttpContext">The current request.</param>
/// <param name="Operation">The operation.</param>
/// <param name="DefinitionId">The resource's definition.</param>
/// <param name="ResourceId">The resource; <see langword="null"/> when creating without a caller-supplied ID.</param>
/// <param name="Resource">The current resource version when the operation reads or changes an existing resource.</param>
public sealed record AsterResourceAuthorizationContext(
    HttpContext HttpContext,
    AsterResourceOperation Operation,
    string DefinitionId,
    string? ResourceId,
    Resource? Resource);

/// <summary>
/// A host-supplied, resource-level authorization hook. It runs after endpoint policies, for every resource an
/// endpoint returns or changes. Aster ships no RBAC: without a registered implementation, every resource is allowed.
/// </summary>
/// <remarks>
/// A denied single-resource request returns <c>403</c>. List and query endpoints drop denied resources from the
/// page instead, so a page can hold fewer items than requested.
/// </remarks>
public interface IAsterResourceAuthorizer
{
    /// <summary>Returns whether the request may perform the operation on the resource.</summary>
    /// <param name="context">The request, operation and resource.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<bool> AuthorizeAsync(AsterResourceAuthorizationContext context, CancellationToken cancellationToken);
}

internal sealed class DefaultTenantScopeAccessor : ITenantScopeAccessor
{
    public static readonly DefaultTenantScopeAccessor Instance = new();

    public TenantScope GetTenantScope(HttpContext httpContext) => TenantScope.Default;
}

internal sealed class AllowAllResourceAuthorizer : IAsterResourceAuthorizer
{
    public static readonly AllowAllResourceAuthorizer Instance = new();

    public ValueTask<bool> AuthorizeAsync(AsterResourceAuthorizationContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}
