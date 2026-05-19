using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SchemaVersions;

public sealed class ResourceSchemaVersionServiceTests : IDisposable
{
    private readonly string sqliteDatabasePath =
        Path.Combine(Path.GetTempPath(), $"aster-schema-versions-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(sqliteDatabasePath);
        TryDelete($"{sqliteDatabasePath}-shm");
        TryDelete($"{sqliteDatabasePath}-wal");
    }

    [Fact]
    public async Task CreateAndUpdate_PreserveDefinitionVersionLineage()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");
        var v2 = await manager.UpdateAsync(v1.ResourceId, new UpdateResourceRequest
        {
            BaseVersion = v1.Version,
            AspectUpdates = { ["TitleAspect"] = new { Title = "Updated" } },
        });
        var other = await manager.CreateAsync("Product", new CreateResourceRequest());

        Assert.Equal(1, v1.DefinitionVersion);
        Assert.Equal(1, v2.DefinitionVersion);
        Assert.Equal(2, other.DefinitionVersion);
        Assert.Equal(1, (await manager.GetVersionAsync(v1.ResourceId, 1))!.DefinitionVersion);
    }

    [Fact]
    public async Task GetSchemaStatusAsync_ClassifiesPerResourceVersionStatuses()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");

        var current = await service.GetSchemaStatusAsync(CreateResource(definitionVersion: 2));
        var older = await service.GetSchemaStatusAsync(CreateResource(definitionVersion: 1));
        var missingDefinition = await service.GetSchemaStatusAsync(CreateResource(definitionId: "Missing", definitionVersion: 1));
        var missingDefinitionVersion = await service.GetSchemaStatusAsync(CreateResource(definitionVersion: 99));
        var unknown = await service.GetSchemaStatusAsync(CreateResource(definitionVersion: null));

        Assert.Equal(ResourceSchemaStatus.Current, current.Status);
        Assert.Equal(ResourceSchemaStatus.OlderThanLatest, older.Status);
        Assert.Equal(ResourceSchemaStatus.MissingDefinition, missingDefinition.Status);
        Assert.Equal(ResourceSchemaStatus.MissingDefinitionVersion, missingDefinitionVersion.Status);
        Assert.Equal(ResourceSchemaStatus.UnknownResourceLineage, unknown.Status);
        Assert.Equal(2, older.LatestDefinitionVersion);
        Assert.Equal(1, older.RecordedDefinitionVersion);
    }

    [Fact]
    public async Task UpgradeAsync_AppendsNewVersionWithExplicitTargetAndPreservesPreviousVersion()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects =
            {
                ["TitleAspect"] = new { Title = "Alpha" },
            },
        });
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect", "AuditAspect");

        var result = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = v1.Version,
            TargetDefinitionVersion = 2,
            AspectUpdates =
            {
                ["SearchAspect"] = new { Text = "alpha" },
            },
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.NotNull(result.Resource);
        Assert.Equal(2, result.Resource.Version);
        Assert.Equal(2, result.Resource.DefinitionVersion);
        Assert.Equal(1, result.SourceDefinitionVersion);
        Assert.Equal(2, result.TargetDefinitionVersion);
        Assert.True(result.Resource.Aspects.ContainsKey("TitleAspect"));
        Assert.True(result.Resource.Aspects.ContainsKey("SearchAspect"));

        var original = await manager.GetVersionAsync(v1.ResourceId, 1);
        Assert.NotNull(original);
        Assert.Equal(1, original.DefinitionVersion);
        Assert.False(original.Aspects.ContainsKey("SearchAspect"));
    }

    [Fact]
    public async Task UpgradeAsync_DefaultsTargetToLatestDefinitionVersion()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");

        var result = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = v1.Version,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Equal(2, result.TargetDefinitionVersion);
        Assert.Equal(2, result.Resource!.DefinitionVersion);
    }

    [Fact]
    public async Task UpgradeAsync_ReturnsNoOpWhenTargetMatchesSourceLineage()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());

        var result = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = v1.Version,
            TargetDefinitionVersion = 1,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.NoOp, result.Status);
        Assert.Same(v1, result.Resource);
        Assert.Equal(1, result.TargetDefinitionVersion);
        Assert.Empty(result.CarriedForwardAspectKeys);

        var versions = (await manager.GetVersionsAsync(v1.ResourceId)).ToList();
        Assert.Single(versions);
    }

    [Fact]
    public async Task UpgradeAsync_ReportsCarriedForwardUndeclaredAspectData()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "LegacyAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects =
            {
                ["TitleAspect"] = new { Title = "Alpha" },
                ["LegacyAspect"] = new { Value = "keep" },
            },
        });
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");

        var result = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = v1.Version,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Contains("LegacyAspect", result.Resource!.Aspects.Keys);
        var key = Assert.Single(result.CarriedForwardAspectKeys);
        Assert.Equal("LegacyAspect", key);
    }

    [Fact]
    public async Task UpgradeAsync_DoesNotReportExplicitlyUpdatedUndeclaredAspectAsCarriedForward()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "LegacyAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
        {
            InitialAspects =
            {
                ["TitleAspect"] = new { Title = "Alpha" },
                ["LegacyAspect"] = new { Value = "old" },
            },
        });
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");

        var result = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = v1.Version,
            AspectUpdates =
            {
                ["LegacyAspect"] = new { Value = "explicit" },
            },
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Empty(result.CarriedForwardAspectKeys);
        Assert.Equal("explicit", GetAspectValue(result.Resource!, "LegacyAspect"));
    }

    [Fact]
    public async Task UpgradeAsync_UnknownLineageCanUpgradeToExistingTarget()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var legacy = CreateResource(definitionVersion: null);
        await writer.SaveVersionAsync(legacy);

        var result = await service.UpgradeAsync(legacy.ResourceId, new ResourceSchemaUpgradeRequest
        {
            BaseVersion = legacy.Version,
            TargetDefinitionVersion = 1,
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, result.Status);
        Assert.Null(result.SourceDefinitionVersion);
        Assert.Equal(1, result.Resource!.DefinitionVersion);
        Assert.Equal(2, result.Resource.Version);
        Assert.Equal(1, (await manager.GetVersionAsync(legacy.ResourceId, 1))!.Version);
    }

    [Fact]
    public async Task UpgradeAsync_RejectsInvalidTargetsAndStaleBase()
    {
        await using var provider = CreateInMemoryProvider();
        var definitionStore = provider.GetRequiredService<IResourceDefinitionStore>();
        var manager = provider.GetRequiredService<IResourceManager>();
        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
        var v1 = await manager.CreateAsync("Product", new CreateResourceRequest());
        await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");
        var v2 = await service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest { BaseVersion = 1 });

        var older = await Assert.ThrowsAsync<ResourceSchemaUpgradeException>(() =>
            service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
            {
                BaseVersion = v2.Resource!.Version,
                TargetDefinitionVersion = 1,
            }).AsTask());
        var tooNew = await Assert.ThrowsAsync<ResourceSchemaUpgradeException>(() =>
            service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
            {
                BaseVersion = v2.Resource!.Version,
                TargetDefinitionVersion = 99,
            }).AsTask());
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            service.UpgradeAsync(v1.ResourceId, new ResourceSchemaUpgradeRequest
            {
                BaseVersion = 1,
                TargetDefinitionVersion = 2,
            }).AsTask());

        Assert.Equal("target-definition-version-before-source", older.Code);
        Assert.Equal("target-definition-version-too-new", tooNew.Code);
    }

    [Fact]
    public async Task AddAsterCore_RegistersSchemaVersionService()
    {
        await using var provider = CreateInMemoryProvider();

        var service = provider.GetRequiredService<IResourceSchemaVersionService>();

        Assert.IsType<ResourceSchemaVersionService>(service);
    }

    [Fact]
    public async Task SqliteProvider_PersistsDefinitionLineageAndSupportsUpgradeAcrossProviderInstances()
    {
        await using (var first = CreateSqliteProvider())
        {
            var definitionStore = first.GetRequiredService<IResourceDefinitionStore>();
            var manager = first.GetRequiredService<IResourceManager>();

            await RegisterProductDefinitionAsync(definitionStore, "TitleAspect");
            var v1 = await manager.CreateAsync("Product", new CreateResourceRequest
            {
                ResourceId = "product-1",
                InitialAspects = { ["TitleAspect"] = new { Title = "Alpha" } },
            });
            await RegisterProductDefinitionAsync(definitionStore, "TitleAspect", "SearchAspect");

            Assert.Equal(1, v1.DefinitionVersion);
        }

        await using var second = CreateSqliteProvider();
        var secondManager = second.GetRequiredService<IResourceManager>();
        var service = second.GetRequiredService<IResourceSchemaVersionService>();
        var loaded = await secondManager.GetLatestVersionAsync("product-1");

        Assert.NotNull(loaded);
        Assert.Equal(ResourceSchemaStatus.OlderThanLatest, (await service.GetSchemaStatusAsync(loaded)).Status);

        var upgraded = await service.UpgradeAsync("product-1", new ResourceSchemaUpgradeRequest
        {
            BaseVersion = loaded.Version,
            AspectUpdates = { ["SearchAspect"] = new { Text = "alpha" } },
        });

        Assert.Equal(ResourceSchemaUpgradeStatus.Upgraded, upgraded.Status);
        Assert.Equal(2, upgraded.Resource!.DefinitionVersion);
        Assert.Equal(2, upgraded.Resource.Version);
    }

    private ServiceProvider CreateInMemoryProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    private ServiceProvider CreateSqliteProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={sqliteDatabasePath}";
            })
            .BuildServiceProvider();

    private static async Task RegisterProductDefinitionAsync(
        IResourceDefinitionStore definitionStore,
        params string[] aspectKeys)
    {
        var definition = new ResourceDefinition
        {
            DefinitionId = "Product",
            Id = Guid.NewGuid().ToString(),
            Version = 0,
            AspectDefinitions = aspectKeys.ToDictionary(
                static key => key,
                static key => new AspectDefinition
                {
                    AspectDefinitionId = key,
                    Id = Guid.NewGuid().ToString(),
                    Version = 1,
                },
                StringComparer.Ordinal),
        };

        await definitionStore.RegisterDefinitionAsync(definition);
    }

    private static Resource CreateResource(
        string resourceId = "product-1",
        string definitionId = "Product",
        int? definitionVersion = 1,
        int version = 1) =>
        new()
        {
            ResourceId = resourceId,
            Id = $"{resourceId}-v{version}",
            DefinitionId = definitionId,
            DefinitionVersion = definitionVersion,
            Version = version,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>
            {
                ["TitleAspect"] = new { Title = "Alpha" },
            },
        };

    private static string? GetAspectValue(Resource resource, string key) =>
        resource.Aspects[key].GetType().GetProperty("Value")?.GetValue(resource.Aspects[key]) as string;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
