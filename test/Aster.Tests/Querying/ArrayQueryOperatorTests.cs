using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.SqliteJson;
using Aster.Tests.Persistence;
using Aster.Tests.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

/// <summary>
/// #83: the portable array operators ArrayContains, ArrayContainsAny and ArrayContainsAll, plus
/// Exists over arrays, return identical results in-memory, in SQLite and in PostgreSQL. Fixtures are written
/// straight through the version writer, so they include shapes validation would reject today
/// (scalars where arrays are expected, nulls, mixed element types), as legacy or imported data can.
/// </summary>
public sealed class ArrayQueryOperatorTests : IDisposable
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

    private async Task<ServiceProvider> CreateSeededProviderAsync(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        else if (providerKind == Postgres)
            services.AddAsterPostgresJsonb(options => options.ConnectionString = postgres.ConnectionString);

        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();

        await SaveAsync(writer, "wf-1", new Dictionary<string, object?>
        {
            ["Tags"] = new[] { "billing", "ops" },
            ["Scores"] = new[] { 1, 2, 3 },
            ["Owners"] = new[] { OwnerA },
            ["Due"] = new[] { "2026-10-20T00:00:00Z" },
            ["Flags"] = new[] { true },
        });
        await SaveAsync(writer, "wf-2", new Dictionary<string, object?>
        {
            ["Tags"] = new[] { "Billing", "billing", "SALES" },
            ["Scores"] = new object[] { 2.5m, 10 },
            ["Owners"] = new[] { OwnerB.ToString("D").ToUpperInvariant() },
            ["Due"] = new[] { "2026-10-20T02:00:00+02:00" },
            ["Flags"] = new[] { false },
        });
        await SaveAsync(writer, "wf-3", new Dictionary<string, object?> { ["Tags"] = Array.Empty<string>(), ["Scores"] = Array.Empty<int>() });
        await SaveAsync(writer, "wf-4", new Dictionary<string, object?> { ["Tags"] = null });
        await SaveAsync(writer, "wf-5", new Dictionary<string, object?> { ["Other"] = "x" });
        await SaveAsync(writer, "wf-6", new Dictionary<string, object?> { ["Tags"] = "billing", ["Scores"] = 2 });
        await SaveAsync(writer, "wf-7", aspect: null);
        await SaveAsync(writer, "wf-8", JsonSerializer.Deserialize<JsonElement>("""{"Tags":["ops",3,true,{"x":1},null]}"""));

        return provider;
    }

    private static ValueTask<Resource> SaveAsync(IResourceVersionWriter writer, string resourceId, object? aspect) =>
        writer.SaveVersionAsync(new Resource
        {
            ResourceId = resourceId,
            Id = $"{resourceId}-v1",
            DefinitionId = "Workflow",
            DefinitionVersion = 1,
            Version = 1,
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Aspects = aspect is null ? [] : new Dictionary<string, object> { ["Labels"] = aspect },
        });

    private static async Task<string[]> QueryIdsAsync(IServiceProvider provider, FilterExpression filter) =>
        (await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            DefinitionId = "Workflow",
            Filter = filter,
        }))
        .Select(static resource => resource.ResourceId)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static FacetValueFilter Labels(string facet, object value, ComparisonOperator op) => new("Labels", facet, value, op);

    public static TheoryData<string, string, object, ComparisonOperator, string[]> Cases()
    {
        var data = new TheoryData<string, string, object, ComparisonOperator, string[]>();
        foreach (var provider in new[] { InMemory, Sqlite, Postgres })
        {
            // Text: case-insensitive, scalars never match, numbers and booleans are not text.
            data.Add(provider, "Tags", "billing", ComparisonOperator.ArrayContains, ["wf-1", "wf-2"]);
            data.Add(provider, "Tags", "BILLING", ComparisonOperator.ArrayContains, ["wf-1", "wf-2"]);
            data.Add(provider, "Tags", "3", ComparisonOperator.ArrayContains, []);
            data.Add(provider, "Tags", "missing", ComparisonOperator.ArrayContains, []);
            data.Add(provider, "Tags", new[] { "sales", "ops" }, ComparisonOperator.ArrayContainsAny, ["wf-1", "wf-2", "wf-8"]);
            data.Add(provider, "Tags", new[] { "billing", "ops" }, ComparisonOperator.ArrayContainsAll, ["wf-1"]);
            data.Add(provider, "Tags", new[] { "billing", "billing" }, ComparisonOperator.ArrayContainsAll, ["wf-1", "wf-2"]);
            data.Add(provider, "Tags", new[] { "ops", "billing" }, ComparisonOperator.ArrayContainsAll, ["wf-1"]);

            // Mixed-element arrays: candidates match only elements of their own kind.
            data.Add(provider, "Tags", 3, ComparisonOperator.ArrayContains, ["wf-8"]);
            data.Add(provider, "Tags", true, ComparisonOperator.ArrayContains, ["wf-8"]);
            data.Add(provider, "Tags", new object[] { "ops", 3, true }, ComparisonOperator.ArrayContainsAll, ["wf-8"]);

            // Numeric: compared by value across integer and decimal representations.
            data.Add(provider, "Scores", 2, ComparisonOperator.ArrayContains, ["wf-1"]);
            data.Add(provider, "Scores", 2L, ComparisonOperator.ArrayContains, ["wf-1"]);
            data.Add(provider, "Scores", 2.5m, ComparisonOperator.ArrayContains, ["wf-2"]);
            data.Add(provider, "Scores", 10.0, ComparisonOperator.ArrayContains, ["wf-2"]);
            data.Add(provider, "Scores", new object[] { 10, 99 }, ComparisonOperator.ArrayContainsAny, ["wf-2"]);
            data.Add(provider, "Scores", new[] { 1, 2, 3 }, ComparisonOperator.ArrayContainsAll, ["wf-1"]);
            data.Add(provider, "Scores", "2", ComparisonOperator.ArrayContains, []);

            // GUIDs: compared as text, so formatting and case do not matter.
            data.Add(provider, "Owners", OwnerA, ComparisonOperator.ArrayContains, ["wf-1"]);
            data.Add(provider, "Owners", OwnerB, ComparisonOperator.ArrayContains, ["wf-2"]);
            data.Add(provider, "Owners", OwnerB.ToString().ToUpperInvariant(), ComparisonOperator.ArrayContains, ["wf-2"]);

            // Date/times: compared as UTC instants.
            data.Add(provider, "Due", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), ComparisonOperator.ArrayContains, ["wf-1", "wf-2"]);
            data.Add(provider, "Due", new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.FromHours(-4)), ComparisonOperator.ArrayContains, ["wf-1", "wf-2"]);
            data.Add(provider, "Due", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), ComparisonOperator.ArrayContains, []);

            // Booleans.
            data.Add(provider, "Flags", false, ComparisonOperator.ArrayContains, ["wf-2"]);
            data.Add(provider, "Flags", new[] { true, false }, ComparisonOperator.ArrayContainsAny, ["wf-1", "wf-2"]);

            // Exists: missing, null, and empty arrays do not exist; scalars and non-empty arrays do.
            data.Add(provider, "Tags", true, ComparisonOperator.Exists, ["wf-1", "wf-2", "wf-6", "wf-8"]);
            data.Add(provider, "Scores", true, ComparisonOperator.Exists, ["wf-1", "wf-2", "wf-6"]);
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task ArrayOperators_ReturnTheSameResultsOnEveryProvider(
        string providerKind,
        string facet,
        object value,
        ComparisonOperator op,
        string[] expected)
    {
        await using var provider = await CreateSeededProviderAsync(providerKind);

        Assert.Equal(expected, await QueryIdsAsync(provider, Labels(facet, value, op)));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task ArrayOperators_ComposeWithLogicalOperators(string providerKind)
    {
        await using var provider = await CreateSeededProviderAsync(providerKind);

        var ids = await QueryIdsAsync(provider, TypedQuery.And(
            Labels("Tags", "billing", ComparisonOperator.ArrayContains),
            TypedQuery.Not(Labels("Tags", "ops", ComparisonOperator.ArrayContains))));

        Assert.Equal(new[] { "wf-2" }, ids);
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task NumericArrayOperators_CompareLargeIntegersExactly(string providerKind)
    {
        // 2^53 + 1 and 2^53 are equal as doubles; 64-bit IDs must still match exactly.
        await using var provider = await CreateSeededProviderAsync(providerKind);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await SaveAsync(writer, "big-1", new Dictionary<string, object?> { ["Ids"] = new[] { 9007199254740993L } });
        await SaveAsync(writer, "big-2", new Dictionary<string, object?> { ["Ids"] = new[] { 9007199254740992L } });

        Assert.Equal(new[] { "big-1" }, await QueryIdsAsync(provider, Labels("Ids", 9007199254740993L, ComparisonOperator.ArrayContains)));
        Assert.Equal(new[] { "big-2" }, await QueryIdsAsync(provider, Labels("Ids", 9007199254740992m, ComparisonOperator.ArrayContains)));
        Assert.Equal(new[] { "big-1" }, await QueryIdsAsync(provider, Labels("Ids", new object[] { 9007199254740993UL, 1L }, ComparisonOperator.ArrayContainsAny)));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task ArrayContains_WorksOnTypedAspectPayloads(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        else if (providerKind == Postgres)
            services.AddAsterPostgresJsonb(options => options.ConnectionString = postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(new Resource
        {
            ResourceId = "typed",
            Id = "typed-v1",
            DefinitionId = "Workflow",
            DefinitionVersion = 1,
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object> { ["LabelsAspect"] = new LabelsAspect { Tags = ["billing"] } },
        });

        var filter = TypedQuery.For<LabelsAspect>().Facet(static aspect => aspect.Tags).ArrayContains("Billing");

        Assert.Equal(new[] { "typed" }, await QueryIdsAsync(provider, filter));
    }

    private sealed class LabelsAspect
    {
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Validation
    // ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, object?, ComparisonOperator, string, string> InvalidCases()
    {
        var data = new TheoryData<string, object?, ComparisonOperator, string, string>();
        foreach (var provider in new[] { InMemory, Sqlite, Postgres })
        {
            data.Add(provider, new[] { "a" }, ComparisonOperator.ArrayContains, "array-value-required", "Filter.Value");
            data.Add(provider, null, ComparisonOperator.ArrayContains, "array-value-required", "Filter.Value");
            data.Add(provider, new { Name = "a" }, ComparisonOperator.ArrayContains, "array-value-required", "Filter.Value");
            data.Add(provider, "a", ComparisonOperator.ArrayContainsAny, "array-values-required", "Filter.Value");
            data.Add(provider, 3, ComparisonOperator.ArrayContainsAll, "array-values-required", "Filter.Value");
            data.Add(provider, Array.Empty<string>(), ComparisonOperator.ArrayContainsAny, "empty-array-values", "Filter.Value");
            data.Add(provider, new object?[] { "a", null }, ComparisonOperator.ArrayContainsAll, "unsupported-array-value-shape", "Filter.Value[1]");
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(InvalidCases))]
    public async Task ArrayOperators_InvalidValueShapes_FailValidationWithStableCodeAndPath(
        string providerKind,
        object? value,
        ComparisonOperator op,
        string code,
        string path)
    {
        await using var provider = await CreateSeededProviderAsync(providerKind);
        var query = new ResourceQuery { DefinitionId = "Workflow", Filter = Labels("Tags", value!, op) };

        var failure = Assert.Single(provider.GetRequiredService<IResourceQueryValidator>().Validate(query).Failures);
        Assert.Equal(code, failure.Code);
        Assert.Equal(path, failure.Path);

        var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(async () =>
            await provider.GetRequiredService<IResourceQueryService>().QueryAsync(query));
        Assert.Equal(code, exception.Code);
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    public async Task ArrayOperators_OnMetadataFilters_AreUnsupported(string providerKind)
    {
        await using var provider = await CreateSeededProviderAsync(providerKind);
        var query = new ResourceQuery { Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.ArrayContains) };

        var failure = Assert.Single(provider.GetRequiredService<IResourceQueryValidator>().Validate(query).Failures);

        Assert.Equal("unsupported-comparison-operator", failure.Code);
        Assert.Equal("Filter.Operator", failure.Path);
    }

    [Fact]
    public async Task ArrayOperators_SqliteCandidateLimit_IsDeclaredAndEnforced()
    {
        await using var provider = await CreateSeededProviderAsync(Sqlite);
        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;
        Assert.Equal(100, capabilities.MaxArrayQueryElements);
        var validator = provider.GetRequiredService<IResourceQueryValidator>();

        var atLimit = new ResourceQuery { Filter = Labels("Tags", Enumerable.Range(0, 100).Select(static i => $"t{i}").ToArray(), ComparisonOperator.ArrayContainsAny) };
        var overLimit = new ResourceQuery { Filter = Labels("Tags", Enumerable.Range(0, 101).Select(static i => $"t{i}").ToArray(), ComparisonOperator.ArrayContainsAny) };

        Assert.True(validator.Validate(atLimit).IsValid);
        Assert.Equal("array-values-limit-exceeded", Assert.Single(validator.Validate(overLimit).Failures).Code);
        Assert.Empty(await provider.GetRequiredService<IResourceQueryService>().QueryAsync(atLimit));
    }

    [Fact]
    public async Task ArrayOperators_InMemoryHasNoCandidateLimit()
    {
        await using var provider = await CreateSeededProviderAsync(InMemory);
        var query = new ResourceQuery { Filter = Labels("Tags", Enumerable.Range(0, 500).Select(static i => $"t{i}").Append("ops").ToArray(), ComparisonOperator.ArrayContainsAny) };

        Assert.Null(provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities.MaxArrayQueryElements);
        Assert.Equal(new[] { "wf-1", "wf-8" }, await QueryIdsAsync(provider, query.Filter!));
    }

    [Fact]
    public void TypedQuery_ArrayHelpers_BuildPortableFilters()
    {
        var tags = TypedQuery.For<LabelsAspect>().Facet(static aspect => aspect.Tags);

        Assert.Equal(new FacetValueFilter("LabelsAspect", "Tags", "ops", ComparisonOperator.ArrayContains), tags.ArrayContains("ops"));

        var any = Assert.IsType<FacetValueFilter>(tags.ArrayContainsAny("a", "b"));
        Assert.Equal(ComparisonOperator.ArrayContainsAny, any.Operator);
        Assert.Equal(new[] { "a", "b" }, any.Value);

        var all = Assert.IsType<FacetValueFilter>(tags.ArrayContainsAll(1, 2));
        Assert.Equal(ComparisonOperator.ArrayContainsAll, all.Operator);
        Assert.Equal(new[] { 1, 2 }, all.Value);
    }
}
