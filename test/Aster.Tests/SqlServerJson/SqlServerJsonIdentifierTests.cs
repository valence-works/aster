using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Tests.Persistence;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqlServerJson;

/// <summary>
/// SQL Server compares strings as if padded with trailing spaces, even under a binary collation, so <c>"x"</c> and
/// <c>"x "</c> would be one key. Identifiers with trailing spaces are refused, and an upsert never treats one as
/// another's row.
/// </summary>
public sealed class SqlServerJsonIdentifierTests : IDisposable
{
    private readonly SqlServerTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task CreateAsync_ResourceIdWithTrailingSpace_IsRefused()
    {
        await using var provider = database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(() => TenantScopeTestFixtures.CreateProductAsync(provider, "padded ", "v1"));

        Assert.Null(await provider.GetRequiredService<IResourceManager>().GetLatestVersionAsync("padded"));
    }

    [SkippableFact]
    public async Task UpdateActivationAsync_IdDifferingOnlyByTrailingSpace_DoesNotOverwriteTheOtherRow()
    {
        await using var provider = database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(provider);
        await TenantScopeTestFixtures.CreateProductAsync(provider, "product", "v1");
        var manager = provider.GetRequiredService<IResourceManager>();
        await manager.ActivateAsync("product", 1, "production");
        var writer = provider.GetRequiredService<IResourceVersionWriter>();

        await Assert.ThrowsAnyAsync<Exception>(() => writer.UpdateActivationAsync("product ", "production", new ActivationState
        {
            ResourceId = "product ",
            Channel = "production",
            ActiveVersions = [],
            LastUpdated = DateTime.UtcNow,
        }).AsTask());

        Assert.Equal([1], (await manager.GetActiveVersionsAsync("product", "production")).Select(static resource => resource.Version));
    }
}
