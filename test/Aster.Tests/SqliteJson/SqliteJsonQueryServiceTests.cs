using System.Linq;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonQueryServiceTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-query-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(databasePath);
        TryDelete($"{databasePath}-shm");
        TryDelete($"{databasePath}-wal");
    }

    [Fact]
    public async Task QueryAsync_WithFreshQueryOnlyProvider_InitializesSchemaAndReturnsEmptyResults()
    {
        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = await query.QueryAsync(new ResourceQuery());

        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_MetadataFilters_ReturnMatchingPersistedResources()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", owner: "alice", aspects: new()
        {
            ["Title"] = new { Title = "Alpha" },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", owner: "bob", aspects: new()
        {
            ["Title"] = new { Title = "Beta" },
        }));
        await store.SaveVersionAsync(CreateResource("order-a", "Order", owner: "alice"));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_Scopes_SelectLatestAllActiveAndDraftVersions()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", version: 1));
        await store.SaveVersionAsync(CreateResource("product-a", "Product", version: 2));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", version: 1));
        await store.UpdateActivationAsync("product-a", "Published", new ActivationState
        {
            ResourceId = "product-a",
            Channel = "Published",
            ActiveVersions = [2],
            LastUpdated = DateTime.UtcNow,
        });

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var latest = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("ResourceId")],
        })).ToList();
        var all = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.AllVersions,
            DefinitionId = "Product",
            Sorts = [new SortExpression("ResourceId"), new SortExpression("Version")],
        })).ToList();
        var active = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            DefinitionId = "Product",
        })).ToList();
        var draft = (await query.QueryAsync(new ResourceQuery
        {
            Scope = ResourceVersionScope.Draft,
            DefinitionId = "Product",
            Sorts = [new SortExpression("ResourceId"), new SortExpression("Version")],
        })).ToList();

        Assert.Equal([("product-a", 2), ("product-b", 1)], latest.Select(r => (r.ResourceId, r.Version)).ToList());
        Assert.Equal([("product-a", 1), ("product-a", 2), ("product-b", 1)], all.Select(r => (r.ResourceId, r.Version)).ToList());
        Assert.Equal(("product-a", 2), (active.Single().ResourceId, active.Single().Version));
        Assert.Equal([("product-a", 1), ("product-b", 1)], draft.Select(r => (r.ResourceId, r.Version)).ToList());
    }

    [Fact]
    public async Task QueryAsync_MetadataSortSkipAndTake_AreExecutedInSqlite()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-c", "Product", created: Utc(2026, 1, 3)));
        await store.SaveVersionAsync(CreateResource("product-a", "Product", created: Utc(2026, 1, 1)));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", created: Utc(2026, 1, 2)));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("Created", SortDirection.Descending)],
            Skip = 1,
            Take = 1,
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-b", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_MetadataSorts_UseStableTieBreakerForPaging()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-c", "Product", owner: "same"));
        await store.SaveVersionAsync(CreateResource("product-a", "Product", owner: "same"));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", owner: "same"));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("Owner")],
            Skip = 1,
            Take = 1,
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-b", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_FacetSorts_OrderByTextFacetValue()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Bravo" },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", aspects: new()
        {
            ["Title"] = new { Title = "alpha" },
        }));
        await store.SaveVersionAsync(CreateResource("product-c", "Product"));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("Title", AspectKey: "Title")],
        })).ToList();

        Assert.Equal(["product-b", "product-a", "product-c"], results.Select(r => r.ResourceId).ToList());
    }

    [Fact]
    public async Task QueryAsync_FacetSorts_OrderByNumericFacetValueDescending()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Price"] = new { Amount = 20 },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", aspects: new()
        {
            ["Price"] = new { Amount = 30 },
        }));
        await store.SaveVersionAsync(CreateResource("product-c", "Product"));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Sorts = [new SortExpression("Amount", SortDirection.Descending, AspectKey: "Price")],
        })).ToList();

        Assert.Equal(["product-b", "product-a", "product-c"], results.Select(r => r.ResourceId).ToList());
    }

    [Fact]
    public async Task QueryAsync_AspectPresenceAndFacetFilters_ReadPersistedJson()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Super Gadget" },
            ["Price"] = new { Amount = 20 },
            ["Inventory"] = new { Count = 5 },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Regular Widget" },
            ["Price"] = new { Amount = 30 },
            ["Inventory"] = new { Count = 10 },
        }));
        await store.SaveVersionAsync(CreateResource("product-c", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Another Gadget" },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var filter = new LogicalExpression(LogicalOperator.And, [
            new AspectPresenceFilter("Price"),
            new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
            new FacetValueFilter("Inventory", "Count", 5, ComparisonOperator.Equals),
            new FacetValueFilter("Price", "Amount", new RangeValue(Min: 10, Max: 25), ComparisonOperator.Range),
        ]);
        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = filter,
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_LogicalOrAndNot_AreExecutedInSqlite()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Gadget" },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Widget" },
            ["Archived"] = new { Flag = true },
        }));
        await store.SaveVersionAsync(CreateResource("product-c", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Other" },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Filter = new LogicalExpression(LogicalOperator.And, [
                new LogicalExpression(LogicalOperator.Or, [
                    new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Equals),
                    new FacetValueFilter("Title", "Title", "Widget", ComparisonOperator.Equals),
                ]),
                new LogicalExpression(LogicalOperator.Not, [
                    new AspectPresenceFilter("Archived"),
                ]),
            ]),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_NumericFacetPredicates_IgnoreNonNumericJsonValues()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Price"] = new { Amount = "not a number" },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", aspects: new()
        {
            ["Price"] = new { Amount = 0 },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var equalsResults = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Price", "Amount", 0, ComparisonOperator.Equals),
        })).ToList();
        var rangeResults = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Price", "Amount", new RangeValue(-1, 1), ComparisonOperator.Range),
        })).ToList();
        var notEqualsResults = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Price", "Amount", 0, ComparisonOperator.NotEquals),
        })).ToList();

        Assert.Equal(["product-b"], equalsResults.Select(r => r.ResourceId).ToList());
        Assert.Equal(["product-b"], rangeResults.Select(r => r.ResourceId).ToList());
        Assert.Equal(["product-a"], notEqualsResults.Select(r => r.ResourceId).ToList());
    }

    [Fact]
    public async Task QueryAsync_DateLikeFacetRanges_FilterAcceptedStoredValues()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("event-a", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero) },
        }));
        await store.SaveVersionAsync(CreateResource("event-b", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = new DateTimeOffset(2026, 2, 15, 10, 0, 0, TimeSpan.Zero) },
        }));
        await store.SaveVersionAsync(CreateResource("event-c", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = new DateTimeOffset(2026, 2, 20, 10, 0, 0, TimeSpan.Zero) },
        }));
        await store.SaveVersionAsync(CreateResource("event-d", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero) },
        }));
        await store.SaveVersionAsync(CreateResource("event-invalid-string", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = "not-a-date" },
        }));
        await store.SaveVersionAsync(CreateResource("event-date-only", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = "2026-02-10" },
        }));
        await store.SaveVersionAsync(CreateResource("event-space-separated", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = "2026-02-10 10:00:00" },
        }));
        await store.SaveVersionAsync(CreateResource("event-number", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = 20260210 },
        }));
        await store.SaveVersionAsync(CreateResource("event-null", "Event", aspects: new()
        {
            ["Schedule"] = new { StartsAt = (string?)null },
        }));
        await store.SaveVersionAsync(CreateResource("event-missing-facet", "Event", aspects: new()
        {
            ["Schedule"] = new { EndsAt = new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.Zero) },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var inclusive = await ExecuteDateRangeAsync(
            query,
            new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 20, 10, 0, 0, TimeSpan.Zero));
        var exclusive = await ExecuteDateRangeAsync(
            query,
            new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 20, 10, 0, 0, TimeSpan.Zero),
            includeMin: false,
            includeMax: false);
        var oneSided = await ExecuteDateRangeAsync(
            query,
            min: null,
            max: new DateTimeOffset(2026, 2, 15, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(["event-a", "event-b", "event-c"], inclusive);
        Assert.Equal(["event-b"], exclusive);
        Assert.Equal(["event-a", "event-b"], oneSided);
    }

    [Fact]
    public async Task QueryAsync_DateOnlyStringRangeBound_FailsClosed()
    {
        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery
            {
                Filter = new FacetValueFilter(
                    "Schedule",
                    "StartsAt",
                    new RangeValue("2026-02-01", "2026-02-28"),
                    ComparisonOperator.Range),
            }).AsTask(),
            "unsupported-range-value-shape",
            "value shape",
            "Filter.Value.Min");
    }

    [Fact]
    public async Task QueryAsync_TextComparisons_UseOrdinalIgnoreCaseForNonAsciiValues()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", owner: "Élodie", aspects: new()
        {
            ["Title"] = new { Title = "Crème Gadget" },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var metadataResults = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", "élodie", ComparisonOperator.Equals),
        })).ToList();
        var facetResults = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Title", "Title", "crème", ComparisonOperator.Contains),
        })).ToList();

        Assert.Single(metadataResults);
        Assert.Single(facetResults);
    }

    [Fact]
    public async Task QueryAsync_PortableOperators_AreTranslatedToSqlite()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", owner: "alice", aspects: new()
        {
            ["Title"] = new { Title = "Alpha Gadget" },
            ["Category"] = new { Category = "Electronics" },
        }));
        await store.SaveVersionAsync(CreateResource("product-b", "Product", owner: "bob", aspects: new()
        {
            ["Title"] = new { Title = "Beta Widget" },
            ["Category"] = new { Category = "Hardware" },
        }));
        await store.SaveVersionAsync(CreateResource("product-c", "Product", owner: "carol", aspects: new()
        {
            ["Category"] = new { Category = "Electronics" },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And, [
                new MetadataFilter("Owner", "bob", ComparisonOperator.NotEquals),
                new MetadataFilter("Owner", new[] { "alice", "carol" }, ComparisonOperator.In),
                new FacetValueFilter("Category", "Category", "Hardware", ComparisonOperator.NotEquals),
                new FacetValueFilter("Category", "Category", new[] { "Electronics" }, ComparisonOperator.In),
                new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.StartsWith),
                new FacetValueFilter("Title", "Title", true, ComparisonOperator.Exists),
            ]),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_JsonPathEscaping_SupportsNamedAndSpecialCharacterAspectKeys()
    {
        const string specialAspectKey = "PriceAspect:Sale\\\"Quoted";

        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            [specialAspectKey] = new { Amount = 10 },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.And, [
                new AspectPresenceFilter(specialAspectKey),
                new FacetValueFilter(specialAspectKey, "Amount", 10, ComparisonOperator.Equals),
            ]),
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_MissingAspectOrFacet_DoesNotMatch()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product", aspects: new()
        {
            ["Title"] = new { Title = "Gadget" },
        }));

        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = await query.QueryAsync(new ResourceQuery
        {
            Filter = new FacetValueFilter("Price", "Amount", 10, ComparisonOperator.Equals),
        });

        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_UnsupportedQueryShapes_ThrowTypedException()
    {
        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery
            {
                Filter = new MetadataFilter("Unknown", "value", ComparisonOperator.Equals),
            }).AsTask(),
            "unsupported-metadata-field",
            "metadata field",
            "Filter.Field");

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery
            {
                Scope = ResourceVersionScope.Active,
            }).AsTask(),
            "activation-channel-required",
            "scope",
            "ActivationChannel");

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery { Skip = -1 }).AsTask(),
            "negative-skip",
            "paging",
            "Skip");

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery { Take = -1 }).AsTask(),
            "negative-take",
            "paging",
            "Take");

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery
            {
                Filter = new FacetValueFilter("Price", "Amount", new RangeValue(null, null), ComparisonOperator.Range),
            }).AsTask(),
            "empty-range",
            "value shape",
            "Filter.Value");
    }

    [Fact]
    public async Task QueryAsync_UnsupportedSqliteShapes_MatchValidationFailureDetails()
    {
        await using var provider = CreateServiceProvider();
        var validator = provider.GetRequiredService<IResourceQueryValidator>();
        var queryService = provider.GetRequiredService<IResourceQueryService>();

        await AssertValidationMatchesExecutionAsync(
            validator,
            queryService,
            new ResourceQuery
            {
                Filter = new MetadataFilter("Version", "1", ComparisonOperator.Contains),
            },
            "unsupported-metadata-contains-field",
            "Filter.Field");
        await AssertValidationMatchesExecutionAsync(
            validator,
            queryService,
            new ResourceQuery
            {
                Filter = new MetadataFilter("Created", "2026-01-01", ComparisonOperator.Range),
            },
            "unsupported-comparison-operator",
            "Filter.Operator");
        await AssertValidationMatchesExecutionAsync(
            validator,
            queryService,
            new ResourceQuery
            {
                Filter = new FacetValueFilter(
                    "Schedule",
                    "StartsAt",
                    new RangeValue(DateTime.UtcNow.AddDays(-1), 10),
                    ComparisonOperator.Range),
            },
            "mixed-range-value-shapes",
            "Filter.Value");
    }

    [Fact]
    public async Task QueryAsync_ProviderSpecificUnsupportedFailure_ReportsPath()
    {
        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        await AssertUnsupportedAsync(
            query.QueryAsync(new ResourceQuery
            {
                Filter = new MetadataFilter("Version", "not-an-integer", ComparisonOperator.Equals),
            }).AsTask(),
            "invalid-metadata-value",
            "metadata value",
            "Filter.Value");
    }

    [Fact]
    public async Task QueryAsync_DoesNotUseInMemoryReaderFallback()
    {
        var store = CreateStore();
        await store.SaveVersionAsync(CreateResource("product-a", "Product"));

        var services = CreateServices();
        services.AddSingleton<IResourceVersionReader, ThrowingVersionReader>();

        await using var provider = services.BuildServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        Assert.Single(results);
        Assert.Equal("product-a", results[0].ResourceId);
    }

    [Fact]
    public async Task QueryAsync_AfterValidationAgainstDifferentProvider_AllowsSharedDateLikeRanges()
    {
        var queryShape = new ResourceQuery
        {
            Filter = new FacetValueFilter(
                "Schedule",
                "StartsAt",
                new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                ComparisonOperator.Range),
        };
        var inMemoryValidation = new ResourceQueryValidator([new InMemoryQueryCapabilitiesProvider()])
            .Validate(queryShape);
        await using var provider = CreateServiceProvider();
        var query = provider.GetRequiredService<IResourceQueryService>();

        var sqliteResults = await query.QueryAsync(queryShape);

        Assert.True(inMemoryValidation.IsValid);
        Assert.Empty(sqliteResults);
    }

    [Fact]
    public void SqliteJsonQueryService_DoesNotExposeQueryableApi()
    {
        Assert.False(typeof(IQueryable<Resource>).IsAssignableFrom(typeof(SqliteJsonQueryService)));
    }

    private ServiceProvider CreateServiceProvider() => CreateServices().BuildServiceProvider();

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddAsterCore();
        services.AddAsterSqliteJson(options =>
        {
            options.ConnectionString = $"Data Source={databasePath}";
        });

        return services;
    }

    private SqliteJsonResourceStore CreateStore() =>
        new(new SqliteJsonAsterOptions
        {
            ConnectionString = $"Data Source={databasePath}",
        });

    private static Resource CreateResource(
        string resourceId,
        string definitionId,
        int version = 1,
        string? owner = null,
        DateTime? created = null,
        Dictionary<string, object>? aspects = null) =>
        new()
        {
            ResourceId = resourceId,
            Id = $"{resourceId}-v{version}",
            DefinitionId = definitionId,
            DefinitionVersion = 1,
            Version = version,
            Created = created ?? Utc(2026, 1, version),
            Owner = owner,
            Aspects = aspects ?? [],
        };

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<IReadOnlyList<string>> ExecuteDateRangeAsync(
        IResourceQueryService query,
        object? min,
        object? max,
        bool includeMin = true,
        bool includeMax = true)
    {
        var results = (await query.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Event",
            Filter = new FacetValueFilter(
                "Schedule",
                "StartsAt",
                new RangeValue(min, max, includeMin, includeMax),
                ComparisonOperator.Range),
            Sorts = [new SortExpression("ResourceId")],
        })).ToList();

        return results.Select(resource => resource.ResourceId).ToList();
    }

    private static async Task<UnsupportedQueryFeatureException> AssertUnsupportedAsync(
        Task task,
        string expectedCode,
        string expectedFeature,
        string? expectedPath = null)
    {
        var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(() => task);

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedFeature, exception.Feature);
        Assert.Equal(expectedPath, exception.Path);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        return exception;
    }

    private static async Task AssertValidationMatchesExecutionAsync(
        IResourceQueryValidator validator,
        IResourceQueryService queryService,
        ResourceQuery query,
        string expectedCode,
        string? expectedPath = null)
    {
        var validation = validator.Validate(query);
        var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(
            () => queryService.QueryAsync(query).AsTask());

        Assert.Contains(validation.Failures, failure =>
            failure.Code == exception.Code
            && failure.Feature == exception.Feature
            && failure.Path == exception.Path);
        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedPath, exception.Path);
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

    private sealed class ThrowingVersionReader : IResourceVersionReader
    {
        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SQLite query service should execute SQL directly.");
    }
}
