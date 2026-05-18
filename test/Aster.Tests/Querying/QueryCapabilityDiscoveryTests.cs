using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Querying;

public sealed class QueryCapabilityDiscoveryTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-capabilities-{Guid.NewGuid():N}.db");

    public void Dispose() => TryDelete(databasePath);

    [Fact]
    public void AddAsterCore_RegistersInMemoryCapabilities()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;

        Assert.Equal(InMemoryQueryCapabilitiesProvider.ProviderKey, capabilities.ProviderKey);
        Assert.Equal("In-memory", capabilities.ProviderName);
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.Empty(capabilities.IndexProjections);
    }

    [Fact]
    public void AddAsterSqliteJson_RegistersSqliteCapabilitiesAsActiveProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;
        var validator = provider.GetRequiredService<IResourceQueryValidator>();

        Assert.Equal(SqliteJsonQueryCapabilitiesProvider.ProviderKey, capabilities.ProviderKey);
        Assert.Equal("SQLite JSON", capabilities.ProviderName);
        Assert.True(capabilities.SupportsFacetSorting);
        Assert.Empty(capabilities.IndexProjections);
        Assert.True(validator.Validate(new ResourceQuery
        {
            Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
        }).IsValid);
    }

    [Fact]
    public void Capabilities_ExposeProviderDifferences()
    {
        var inMemory = new Core.InMemory.InMemoryQueryCapabilitiesProvider().Capabilities;
        var sqlite = new SqliteJsonQueryCapabilitiesProvider().Capabilities;

        Assert.True(inMemory.SupportsFacetSorting);
        Assert.True(sqlite.SupportsFacetSorting);
        Assert.Contains(QueryValueShape.DateTime, inMemory.FacetRangeSupport);
        Assert.Contains(QueryValueShape.DateTime, sqlite.FacetRangeSupport);
        Assert.Contains("Version", inMemory.MetadataContainsFields);
        Assert.DoesNotContain("Version", sqlite.MetadataContainsFields);
        Assert.Empty(inMemory.IndexProjections);
        Assert.Empty(sqlite.IndexProjections);
    }

    [Fact]
    public void Validate_UsesActiveProviderKeyInsteadOfEarlierDefaultCapabilities()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={databasePath}";
            })
            .BuildServiceProvider();

        var validator = provider.GetRequiredService<IResourceQueryValidator>();
        var result = validator.Validate(new ResourceQuery
        {
            Filter = new MetadataFilter("Version", "1", ComparisonOperator.Contains),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == "unsupported-metadata-contains-field");
    }

    [Fact]
    public async Task ProviderConsistency_WithSupportedQueries_ValidatesAndExecutes()
    {
        await using var sqliteProvider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={databasePath}";
            })
            .BuildServiceProvider();
        var inMemoryQuery = new InMemoryQueryService(
            new InMemoryResourceStore(),
            NullLogger<InMemoryQueryService>.Instance,
            [new InMemoryQueryCapabilitiesProvider()]);

        await AssertValidatesAndExecutesAsync(
            new ResourceQuery { Sorts = [new SortExpression("Created")] },
            sqliteProvider.GetRequiredService<IResourceQueryValidator>(),
            sqliteProvider.GetRequiredService<IResourceQueryService>());
        await AssertValidatesAndExecutesAsync(
            new ResourceQuery { Sorts = [new SortExpression("Created")] },
            new ResourceQueryValidator([new InMemoryQueryCapabilitiesProvider()]),
            inMemoryQuery);
    }

    [Fact]
    public async Task ProviderConsistency_WithUnsupportedQueries_MatchesValidationAndExecution()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={databasePath}";
            })
            .BuildServiceProvider();

        await AssertValidationMatchesExecutionAsync(
            new ResourceQuery
            {
                Filter = new FacetValueFilter(
                    "Schedule",
                    "StartsAt",
                    new RangeValue(DateTime.UtcNow.AddDays(-1), 10),
                    ComparisonOperator.Range),
            },
            provider.GetRequiredService<IResourceQueryValidator>(),
            provider.GetRequiredService<IResourceQueryService>(),
            "mixed-range-value-shapes");
    }

    private static async Task AssertValidatesAndExecutesAsync(
        ResourceQuery query,
        IResourceQueryValidator validator,
        IResourceQueryService queryService)
    {
        var validation = validator.Validate(query);

        Assert.True(validation.IsValid);
        _ = await queryService.QueryAsync(query);
    }

    private static async Task AssertValidationMatchesExecutionAsync(
        ResourceQuery query,
        IResourceQueryValidator validator,
        IResourceQueryService queryService,
        string expectedCode)
    {
        var validation = validator.Validate(query);
        var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(
            () => queryService.QueryAsync(query).AsTask());

        Assert.Contains(validation.Failures, failure => failure.Code == exception.Code);
        Assert.Equal(expectedCode, exception.Code);
    }

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
