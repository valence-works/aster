using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #130: updates validate against the definition version the resource records
/// (<see cref="Resource.DefinitionVersion"/>), not the latest one, so registering a stricter
/// definition version changes the rules only for new resources and for resources that are
/// explicitly upgraded (spec 012), identically for both providers.
/// </summary>
public sealed class UpdateValidationLineageTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static FacetDefinition Facet(string id, string type, bool isRequired = false) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        IsRequired = isRequired,
    };

    private static ResourceDefinition Definition(params FacetDefinition[] facets) => new()
    {
        DefinitionId = "Product",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Details"] = new()
            {
                AspectDefinitionId = "Details",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions = facets,
            },
        },
    };

    private static Dictionary<string, object> Details(Dictionary<string, object> facets) => new() { ["Details"] = facets };

    private static UpdateResourceRequest ColorUpdate(int baseVersion, string color) => new()
    {
        BaseVersion = baseVersion,
        AspectUpdates = Details(new Dictionary<string, object> { ["Color"] = color }),
    };

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_StricterLatestDefinition_DoesNotApplyToResourcesOnOlderLineage(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string"), Facet("Color", "string")));
        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object> { ["Color"] = "#000000" }),
        });
        Assert.Equal(1, created.DefinitionVersion);

        // Version 2 makes Name required and Color an int.
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true), Facet("Color", "int")));

        var updated = await manager.UpdateAsync(created.ResourceId, ColorUpdate(created.Version, "#ffffff"));

        Assert.Equal(2, updated.Version);
        Assert.Equal(1, updated.DefinitionVersion);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_RecordedLineageRulesStillApply_WhenLatestIsLooser(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true), Facet("Color", "string")));
        var created = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object> { ["Name"] = "Black", ["Color"] = "#000000" }),
        });

        // Version 2 drops the requirement; the resource is still on version 1.
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string"), Facet("Color", "string")));

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync(created.ResourceId, new UpdateResourceRequest
            {
                BaseVersion = created.Version,
                AspectUpdates = Details(new Dictionary<string, object> { ["Color"] = "#ffffff" }),
            }));

        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(exception.Diagnostics).Code);
        Assert.Single(await manager.GetVersionsAsync(created.ResourceId));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task NewRules_ApplyToNewResources_AndAfterExplicitUpgrade(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string"), Facet("Color", "string")));
        var existing = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects = Details(new Dictionary<string, object> { ["Color"] = "#000000" }),
        });
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true), Facet("Color", "string")));

        // A new resource records version 2 and is validated against it.
        var createException = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = Details(new Dictionary<string, object> { ["Color"] = "#000000" }),
            }));
        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(createException.Diagnostics).Code);

        // After an explicit upgrade, the existing resource follows version 2 too.
        var upgraded = await provider.GetRequiredService<IResourceSchemaVersionService>()
            .UpgradeAsync(existing.ResourceId, new ResourceSchemaUpgradeRequest { BaseVersion = existing.Version });
        Assert.Equal(2, upgraded.Resource!.DefinitionVersion);

        var updateException = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.UpdateAsync(existing.ResourceId, ColorUpdate(upgraded.Resource.Version, "#ffffff")));
        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(updateException.Diagnostics).Code);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task UpdateAsync_ResourceWithoutRecordedVersion_ValidatesAgainstLatest(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(Definition(Facet("Name", "string", isRequired: true), Facet("Color", "string")));
        await provider.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(new Resource
        {
            ResourceId = "legacy",
            Id = "legacy-v1",
            DefinitionId = "Product",
            DefinitionVersion = null,
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = Details(new Dictionary<string, object> { ["Color"] = "#000000" }),
        });

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await provider.GetRequiredService<IResourceManager>().UpdateAsync("legacy", ColorUpdate(1, "#ffffff")));

        Assert.Equal(FacetValueDiagnosticCodes.Required, Assert.Single(exception.Diagnostics).Code);
    }
}
