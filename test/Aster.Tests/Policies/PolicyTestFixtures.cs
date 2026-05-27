using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

internal static class PolicyTestFixtures
{
    public static ServiceProvider CreateCoreProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    public static ServiceProvider CreateSqliteProvider(string databasePath) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}")
            .BuildServiceProvider();

    public static ResourcePolicyDeclaration ArchivePolicy(
        string policyId = "archive-old",
        TimeSpan? minimumAge = null) =>
        new()
        {
            PolicyId = policyId,
            Name = "Archive old resources",
            Kind = ResourcePolicyKind.Archival,
            Target = ResourcePolicyTarget.Resource,
            Outcome = ResourcePolicyOutcome.Archive,
            Criteria = new ResourcePolicyCriteria
            {
                MinimumAge = minimumAge ?? TimeSpan.FromDays(30),
                LifecycleState = ResourceLifecycleMarkerState.None,
            },
        };

    public static ResourcePolicyDeclaration SoftDeletePolicy(string policyId = "soft-delete-old") =>
        new()
        {
            PolicyId = policyId,
            Kind = ResourcePolicyKind.SoftDelete,
            Target = ResourcePolicyTarget.Resource,
            Outcome = ResourcePolicyOutcome.SoftDelete,
            Criteria = new ResourcePolicyCriteria
            {
                MinimumAge = TimeSpan.FromDays(30),
                LifecycleState = ResourceLifecycleMarkerState.None,
            },
        };

    public static ResourcePolicyDeclaration PruningPolicy(int retainedVersions = 2) =>
        new()
        {
            PolicyId = "keep-latest",
            Kind = ResourcePolicyKind.VersionPruning,
            Target = ResourcePolicyTarget.ResourceVersion,
            Outcome = ResourcePolicyOutcome.PrunePreview,
            Criteria = new ResourcePolicyCriteria
            {
                MaximumRetainedVersions = retainedVersions,
            },
        };

    public static ResourcePolicyApplicationCandidate ApplicationCandidate(
        string resourceId,
        string policyId = "archive-old",
        ResourcePolicyKind policyKind = ResourcePolicyKind.Archival,
        ResourcePolicyOutcome outcome = ResourcePolicyOutcome.Archive,
        int? resourceVersion = 1) =>
        new()
        {
            PolicyId = policyId,
            PolicyKind = policyKind,
            Outcome = outcome,
            ResourceId = resourceId,
            ResourceVersion = resourceVersion,
        };

    public static ResourceDefinition ProductDefinition(params ResourcePolicyDeclaration[] policies) =>
        new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .ApplyPolicies(policies)
            .Build();

    public static async Task RegisterProductDefinitionAsync(
        IServiceProvider provider,
        TenantScope? tenantScope = null,
        params ResourcePolicyDeclaration[] policies)
    {
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = ProductDefinition(policies);
        if (tenantScope is null)
            await store.RegisterDefinitionAsync(definition);
        else
            await store.RegisterDefinitionAsync(definition, tenantScope, CancellationToken.None);
    }

    public static async Task<Resource> SaveResourceAsync(
        IServiceProvider provider,
        string resourceId,
        int version = 1,
        DateTime? created = null,
        TenantScope? tenantScope = null)
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var resource = new Resource
        {
            TenantScope = tenantScope ?? TenantScope.Default,
            ResourceId = resourceId,
            Id = $"{resourceId}-{version}",
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = version,
            Created = created ?? DateTime.UtcNow,
            Aspects = new Dictionary<string, object>
            {
                ["Title"] = new { Title = resourceId },
            },
        };

        return await writer.SaveVersionAsync(resource);
    }

    public static void DeleteSqliteFiles(string databasePath)
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    private static ResourceDefinitionBuilder ApplyPolicies(
        this ResourceDefinitionBuilder builder,
        IEnumerable<ResourcePolicyDeclaration> policies)
    {
        foreach (var policy in policies)
            builder.WithPolicy(policy);

        return builder;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
