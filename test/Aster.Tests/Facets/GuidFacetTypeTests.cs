using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqliteJson;
using Aster.Tests.Persistence;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// #81: the guid facet type (ADR 0002 §5) validates on save, supports AllowedValues and
/// DefaultValue metadata, and is queryable with Equals, NotEquals, In and Exists on both providers.
/// </summary>
public sealed class GuidFacetTypeTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Postgres = "postgres";

    private static readonly Guid OwnerA = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
    private static readonly Guid OwnerB = Guid.Parse("1b4e28ba-2fa1-11d2-883f-0016d3cca427");

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    private readonly PostgresTestDatabase postgres = new();

    public void Dispose()
    {
        SqliteJsonTestDatabase.DeleteFiles(databasePath);
        postgres.Dispose();
    }

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        else if (providerKind == Postgres)
            services.AddAsterPostgresJsonb(options => options.ConnectionString = postgres.ConnectionString);

        return services.BuildServiceProvider();
    }

    private static ResourceDefinition Definition(FacetMetadata? metadata = null) => new()
    {
        DefinitionId = "Workflow",
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = new Dictionary<string, AspectDefinition>
        {
            ["Ownership"] = new()
            {
                AspectDefinitionId = "Ownership",
                Id = Guid.NewGuid().ToString(),
                Version = 1,
                FacetDefinitions =
                [
                    new FacetDefinition
                    {
                        FacetDefinitionId = "OwnerId",
                        Id = Guid.NewGuid().ToString(),
                        Version = 1,
                        Type = "guid",
                        Metadata = metadata,
                    },
                ],
            },
        },
    };

    private static Dictionary<string, object> Ownership(object? ownerId) =>
        new() { ["Ownership"] = new Dictionary<string, object?> { ["OwnerId"] = ownerId } };

    private static async Task<IResourceManager> RegisterAsync(ServiceProvider provider, FacetMetadata? metadata = null)
    {
        await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(Definition(metadata));
        return provider.GetRequiredService<IResourceManager>();
    }

    public static TheoryData<string, object> ValidValues()
    {
        var data = new TheoryData<string, object>();
        foreach (var providerKind in new[] { InMemory, Sqlite, Postgres })
        {
            data.Add(providerKind, OwnerA);
            data.Add(providerKind, OwnerA.ToString());
            data.Add(providerKind, OwnerA.ToString("D").ToUpperInvariant());
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(ValidValues))]
    public async Task CreateAsync_GuidOrDFormatString_Saves(string providerKind, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider);

        var resource = await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Ownership(value) });

        Assert.Equal(1, resource.Version);
    }

    [SkippableTheory]
    [InlineData(InMemory, "not-a-guid")]
    [InlineData(Sqlite, "not-a-guid")]
    [InlineData(Postgres, "not-a-guid")]
    [InlineData(InMemory, 42)]
    [InlineData(InMemory, "3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData(Sqlite, "3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData(Postgres, "3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData(InMemory, "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData(InMemory, "(3f2504e0-4f89-11d3-9a0c-0305e82c3301)")]
    [InlineData(InMemory, " 3f2504e0-4f89-11d3-9a0c-0305e82c3301 ")]
    [InlineData(Sqlite, "3f2504e0-4f89-11d3-9a0c-0305e82c3301\n")]
    [InlineData(Postgres, "3f2504e0-4f89-11d3-9a0c-0305e82c3301\n")]
    public async Task CreateAsync_NonGuidOrNonDFormatValue_IsTypeMismatch(string providerKind, object value)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Ownership(value) }));

        Assert.Equal(FacetValueDiagnosticCodes.TypeMismatch, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public async Task CreateAsync_JsonElementGuid_Saves()
    {
        await using var provider = CreateProvider(InMemory);
        var manager = await RegisterAsync(provider);
        var payload = JsonSerializer.Deserialize<JsonElement>($$"""{ "OwnerId": "{{OwnerA}}" }""");

        var resource = await manager.CreateAsync("Workflow", new CreateResourceRequest
        {
            InitialAspects = new Dictionary<string, object> { ["Ownership"] = payload },
        });

        Assert.Equal(1, resource.Version);
    }

    [Fact]
    public async Task AllowedValues_ComparesGuidsRegardlessOfFormatting()
    {
        await using var provider = CreateProvider(InMemory);
        var manager = await RegisterAsync(provider, new FacetMetadata { AllowedValues = [OwnerA.ToString()], DefaultValue = OwnerA.ToString().ToUpperInvariant() });

        await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Ownership(OwnerA.ToString().ToUpperInvariant()) });

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(async () =>
            await manager.CreateAsync("Workflow", new CreateResourceRequest { InitialAspects = Ownership(OwnerB) }));
        Assert.Equal(FacetValueDiagnosticCodes.NotAllowed, Assert.Single(exception.Diagnostics).Code);
    }

    [SkippableTheory]
    [InlineData("not-a-guid", FacetMetadataDiagnosticCodes.AllowedValueTypeMismatch)]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301", FacetMetadataDiagnosticCodes.AllowedValueTypeMismatch)]
    public async Task Registration_AllowedValueThatIsNotAGuid_IsRejected(string allowedValue, string expectedCode)
    {
        await using var provider = CreateProvider(InMemory);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await RegisterAsync(provider, new FacetMetadata { AllowedValues = [allowedValue] }));

        Assert.Equal(expectedCode, Assert.Single(exception.Diagnostics).Code);
    }

    [Fact]
    public async Task Registration_LengthOrPatternOnGuidFacet_IsInapplicable()
    {
        await using var provider = CreateProvider(InMemory);

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await RegisterAsync(provider, new FacetMetadata { MaxLength = 36, Pattern = "^[a-f0-9-]+$" }));

        Assert.All(exception.Diagnostics, static diagnostic => Assert.Equal(FacetMetadataDiagnosticCodes.ConstraintInapplicable, diagnostic.Code));
        Assert.Equal(2, exception.Diagnostics.Count);
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task Query_GuidFacet_SupportsEqualsNotEqualsInAndExists(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var manager = await RegisterAsync(provider);
        await manager.CreateAsync("Workflow", new CreateResourceRequest { ResourceId = "wf-a", InitialAspects = Ownership(OwnerA) });
        await manager.CreateAsync("Workflow", new CreateResourceRequest { ResourceId = "wf-b", InitialAspects = Ownership(OwnerB.ToString().ToUpperInvariant()) });
        await manager.CreateAsync("Workflow", new CreateResourceRequest { ResourceId = "wf-none", InitialAspects = Ownership(null) });
        var queries = provider.GetRequiredService<IResourceQueryService>();

        async Task<string[]> Ids(ComparisonOperator op, object value) =>
            (await queries.QueryAsync(new ResourceQuery
            {
                DefinitionId = "Workflow",
                Filter = new FacetValueFilter("Ownership", "OwnerId", value, op),
            })).Select(static resource => resource.ResourceId).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "wf-a" }, await Ids(ComparisonOperator.Equals, OwnerA));
        Assert.Equal(new[] { "wf-b" }, await Ids(ComparisonOperator.Equals, OwnerB));
        Assert.Equal(new[] { "wf-a" }, await Ids(ComparisonOperator.Equals, OwnerA.ToString().ToUpperInvariant()));
        Assert.DoesNotContain("wf-a", await Ids(ComparisonOperator.NotEquals, OwnerA));
        Assert.Contains("wf-b", await Ids(ComparisonOperator.NotEquals, OwnerA));
        Assert.Equal(new[] { "wf-a", "wf-b" }, await Ids(ComparisonOperator.In, new[] { OwnerA, OwnerB }));
        Assert.Equal(new[] { "wf-a", "wf-b" }, await Ids(ComparisonOperator.Exists, true));
    }
}
