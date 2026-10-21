using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;
using Elsa.Workflows;
using ITenantAccessor = Elsa.Common.Multitenancy.ITenantAccessor;

namespace Aster.Elsa.Activities;

/// <summary>Shared plumbing of the Aster activities.</summary>
internal static class AsterActivityContext
{
    /// <summary>The Aster tenant scope for the Elsa tenant the activity runs in, read on every call.</summary>
    public static TenantScope GetAsterTenantScope(this ActivityExecutionContext context) =>
        AsterTenantMapping.FromTenantAccessor(context.GetService<ITenantAccessor>());

    /// <summary>The value of a required string input, or a fault with <paramref name="code"/> when it is empty.</summary>
    public static string Required(string? value, string code, string inputName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AsterActivityException(code, $"The {inputName} input is required.")
            : value;

    /// <summary>The latest version of a resource, or a <c>resource-not-found</c> fault.</summary>
    public static async ValueTask<Resource> RequireLatestAsync(
        IResourceManager manager, string resourceId, TenantScope tenant, CancellationToken cancellationToken) =>
        await manager.GetLatestVersionAsync(resourceId, tenant, cancellationToken)
            ?? throw ResourceNotFound(resourceId);

    /// <summary>A version of a resource, or a <c>resource-not-found</c> or <c>version-not-found</c> fault.</summary>
    public static async ValueTask<Resource> RequireVersionAsync(
        IResourceManager manager, string resourceId, int version, TenantScope tenant, CancellationToken cancellationToken)
    {
        if (await manager.GetVersionAsync(resourceId, version, tenant, cancellationToken) is { } resource)
            return resource;

        await RequireLatestAsync(manager, resourceId, tenant, cancellationToken);
        throw new AsterActivityException(AsterActivityFaultCodes.VersionNotFound, $"Version {version} of resource '{resourceId}' was not found.");
    }

    public static AsterActivityException ResourceNotFound(string resourceId) =>
        new(AsterActivityFaultCodes.ResourceNotFound, $"Resource '{resourceId}' was not found.");
}
