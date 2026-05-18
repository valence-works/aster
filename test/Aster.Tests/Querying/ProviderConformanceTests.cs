using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Querying;

public sealed class ProviderConformanceTests : IDisposable
{
    private readonly string sqliteDatabasePath =
        Path.Combine(Path.GetTempPath(), $"aster-conformance-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        TryDelete(sqliteDatabasePath);
        TryDelete($"{sqliteDatabasePath}-shm");
        TryDelete($"{sqliteDatabasePath}-wal");
    }

    [Fact]
    public async Task BuiltInProviders_PassSharedConformanceSuite()
    {
        await using var inMemoryProvider = await CreateInMemoryProviderAsync();
        await using var sqliteProvider = await CreateSqliteProviderAsync();
        var inMemory = new ProviderConformanceSubject(
            "In-memory",
            InMemoryQueryCapabilitiesProvider.ProviderKey,
            inMemoryProvider,
            RequiresNonEmptyResults: true);
        var sqlite = new ProviderConformanceSubject(
            "SQLite JSON",
            SqliteJsonQueryCapabilitiesProvider.ProviderKey,
            sqliteProvider,
            RequiresNonEmptyResults: true);

        await ProviderConformanceSuite.AssertConformsAsync(inMemory);
        await ProviderConformanceSuite.AssertConformsAsync(sqlite);
    }

    [Fact]
    public async Task CustomProvider_CanOptIntoSharedConformanceSuite()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<ConformingCustomQueryService, MinimalCustomCapabilitiesProvider>()
            .BuildServiceProvider();
        var subject = new ProviderConformanceSubject(
            "Minimal custom",
            MinimalCustomCapabilitiesProvider.ProviderKey,
            provider);

        await ProviderConformanceSuite.AssertConformsAsync(subject);
    }

    [Fact]
    public async Task ConformanceSuite_FailsClosedWhenCapabilitiesDoNotMatchActiveProvider()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<ConformingCustomQueryService>()
            .AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<ConformingCustomQueryService>())
            .AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<ConformingCustomQueryService>())
            .BuildServiceProvider();
        var subject = new ProviderConformanceSubject(
            "Missing custom capabilities",
            MinimalCustomCapabilitiesProvider.ProviderKey,
            provider);

        var failures = await ProviderConformanceSuite.EvaluateAsync(subject);

        var failure = Assert.Single(failures);
        Assert.Equal("Provider identity", failure.Area);
        Assert.Contains("matching capability declaration", failure.Message);
    }

    [Fact]
    public async Task ConformanceSuite_DetectsProvidersThatAcceptUnsupportedQueries()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<NonValidatingCustomQueryService, MinimalCustomCapabilitiesProvider>()
            .BuildServiceProvider();
        var subject = new ProviderConformanceSubject(
            "Non-validating custom",
            MinimalCustomCapabilitiesProvider.ProviderKey,
            provider);

        var failures = await ProviderConformanceSuite.EvaluateAsync(subject);

        Assert.Contains(
            failures,
            failure => failure.Area == "Execution"
                && failure.Message.Contains("accepted unsupported query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConformanceSuite_DetectsProvidersThatRejectSupportedQueries()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<AlwaysRejectingCustomQueryService, MinimalCustomCapabilitiesProvider>()
            .BuildServiceProvider();
        var subject = new ProviderConformanceSubject(
            "Always-rejecting custom",
            MinimalCustomCapabilitiesProvider.ProviderKey,
            provider);

        var failures = await ProviderConformanceSuite.EvaluateAsync(subject);

        Assert.Contains(
            failures,
            failure => failure.Area == "Execution"
                && failure.Message.Contains("rejected supported query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConformanceSuite_DetectsInvalidIndexProjectionDeclarations()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<ConformingCustomQueryService, InvalidIndexProjectionCapabilitiesProvider>()
            .BuildServiceProvider();
        var subject = new ProviderConformanceSubject(
            "Invalid index projections",
            MinimalCustomCapabilitiesProvider.ProviderKey,
            provider);

        var failures = await ProviderConformanceSuite.EvaluateAsync(subject);

        Assert.Contains(
            failures,
            failure => failure.Area == "Index projections"
                && failure.Message.Contains(IndexProjectionFailureCodes.DuplicateProjectionField, StringComparison.Ordinal));
    }

    private static async Task<ServiceProvider> CreateInMemoryProviderAsync()
    {
        var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        await SeedAsync(provider);

        return provider;
    }

    private async Task<ServiceProvider> CreateSqliteProviderAsync()
    {
        var provider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = $"Data Source={sqliteDatabasePath}";
            })
            .BuildServiceProvider();
        await SeedAsync(provider);

        return provider;
    }

    private static async Task SeedAsync(IServiceProvider provider)
    {
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await writer.SaveVersionAsync(CreateResource(
            "product-a",
            "Product",
            version: 1,
            owner: "alice",
            created: Utc(2026, 1, 1),
            aspects: new()
            {
                ["Title"] = new { Title = "Alpha Gadget" },
                ["Price"] = new { Amount = 20 },
                ["Inventory"] = new { Count = 5 },
                ["Schedule"] = new { StartsAt = Utc(2026, 2, 1) },
            }));
        await writer.SaveVersionAsync(CreateResource(
            "product-a",
            "Product",
            version: 2,
            owner: "alice",
            created: Utc(2026, 1, 2),
            aspects: new()
            {
                ["Title"] = new { Title = "Alpha Gadget Updated" },
                ["Price"] = new { Amount = 25 },
                ["Inventory"] = new { Count = 10 },
                ["Schedule"] = new { StartsAt = Utc(2026, 2, 2) },
            }));
        await writer.SaveVersionAsync(CreateResource(
            "product-b",
            "Product",
            owner: "bob",
            created: Utc(2026, 1, 3),
            aspects: new()
            {
                ["Title"] = new { Title = "Beta Widget" },
                ["Price"] = new { Amount = 35 },
            }));
        await writer.SaveVersionAsync(CreateResource(
            "order-a",
            "Order",
            owner: "alice",
            created: Utc(2026, 1, 4)));
        await writer.UpdateActivationAsync("product-a", "Published", new ActivationState
        {
            ResourceId = "product-a",
            Channel = "Published",
            ActiveVersions = [2],
            LastUpdated = Utc(2026, 1, 5),
        });
    }

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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ConformingCustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        private readonly ResourceQueryValidator validator;

        public ConformingCustomQueryService(IEnumerable<IResourceQueryCapabilitiesProvider> capabilityProviders)
        {
            validator = new(capabilityProviders, this);
        }

        public string ProviderKey => MinimalCustomCapabilitiesProvider.ProviderKey;

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default)
        {
            var validation = validator.Validate(query);
            if (!validation.IsValid)
                throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);

            return new([]);
        }
    }

    private sealed class NonValidatingCustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public string ProviderKey => MinimalCustomCapabilitiesProvider.ProviderKey;

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            new([]);
    }

    private sealed class AlwaysRejectingCustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public string ProviderKey => MinimalCustomCapabilitiesProvider.ProviderKey;

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            throw new UnsupportedQueryFeatureException(
                "custom-rejected-supported-query",
                "query",
                "The custom provider rejected every query.");
    }

    private sealed class MinimalCustomCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public const string ProviderKey = "minimal-custom";

        public QueryCapabilityDescription Capabilities { get; } = new(
            ProviderKey: ProviderKey,
            ProviderName: "Minimal custom",
            SupportedScopes: new HashSet<ResourceVersionScope> { ResourceVersionScope.Latest },
            RequiresActivationChannelForActiveScope: false,
            SupportedFilterTypes: new HashSet<QueryFilterType>(),
            SupportedLogicalOperators: new HashSet<LogicalOperator>(),
            SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
            SupportedMetadataFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            MetadataContainsFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SupportsMetadataSorting: false,
            SupportsFacetSorting: false,
            SupportsSkip: false,
            SupportsTake: false,
            FacetRangeSupport: new HashSet<QueryValueShape>(),
            UnsupportedFeatures:
            [
                "Filtering",
                "Sorting",
                "Paging",
            ]);
    }

    private sealed class InvalidIndexProjectionCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = new(
            ProviderKey: MinimalCustomCapabilitiesProvider.ProviderKey,
            ProviderName: "Invalid index projections",
            SupportedScopes: new HashSet<ResourceVersionScope> { ResourceVersionScope.Latest },
            RequiresActivationChannelForActiveScope: false,
            SupportedFilterTypes: new HashSet<QueryFilterType>(),
            SupportedLogicalOperators: new HashSet<LogicalOperator>(),
            SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
            SupportedMetadataFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            MetadataContainsFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SupportsMetadataSorting: false,
            SupportsFacetSorting: false,
            SupportsSkip: false,
            SupportsTake: false,
            FacetRangeSupport: new HashSet<QueryValueShape>(),
            UnsupportedFeatures: [],
            IndexProjections:
            [
                IndexProjection.Metadata("duplicate", "ResourceId", IndexFieldType.Keyword),
                IndexProjection.Facet("duplicate", "Title", "Title", IndexFieldType.Keyword),
            ]);
    }
}

internal sealed record ProviderConformanceSubject(
    string Name,
    string ExpectedProviderKey,
    IServiceProvider Services,
    bool RequiresNonEmptyResults = false);

internal sealed record ProviderConformanceFailure(
    string ProviderName,
    string CaseName,
    string Area,
    string Message);

internal static class ProviderConformanceSuite
{
    public static async Task AssertConformsAsync(ProviderConformanceSubject subject)
    {
        var failures = await EvaluateAsync(subject);

        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine, failures.Select(static failure =>
                $"{failure.ProviderName} / {failure.CaseName} / {failure.Area}: {failure.Message}")));
    }

    public static async Task<IReadOnlyList<ProviderConformanceFailure>> EvaluateAsync(
        ProviderConformanceSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var failures = new List<ProviderConformanceFailure>();
        var identity = subject.Services.GetService<IResourceQueryProviderIdentity>();
        var capabilities = subject.Services.GetServices<IResourceQueryCapabilitiesProvider>()
            .Select(provider => provider.Capabilities)
            .LastOrDefault(capabilities =>
                string.Equals(capabilities.ProviderKey, subject.ExpectedProviderKey, StringComparison.Ordinal));

        if (identity?.ProviderKey != subject.ExpectedProviderKey || capabilities is null)
        {
            failures.Add(new(
                subject.Name,
                "Provider registration",
                "Provider identity",
                $"Expected active provider key '{subject.ExpectedProviderKey}' with a matching capability declaration."));
            return failures;
        }

        var indexProjectionValidation = new IndexProjectionValidator().Validate(capabilities.IndexProjections);
        if (!indexProjectionValidation.IsValid)
        {
            failures.Add(new(
                subject.Name,
                "Index projection declarations",
                "Index projections",
                $"Invalid index projection declarations: {FailureCodes(indexProjectionValidation.Failures)}."));
        }

        var validator = subject.Services.GetRequiredService<IResourceQueryValidator>();
        var queryService = subject.Services.GetRequiredService<IResourceQueryService>();

        foreach (var queryCase in SupportedCases(capabilities))
            await EvaluateSupportedCaseAsync(subject, validator, queryService, queryCase, failures);

        foreach (var queryCase in UnsupportedCases(capabilities))
            await EvaluateUnsupportedCaseAsync(subject, validator, queryService, queryCase, failures);

        return failures;
    }

    private static async Task EvaluateSupportedCaseAsync(
        ProviderConformanceSubject subject,
        IResourceQueryValidator validator,
        IResourceQueryService queryService,
        ProviderQueryCase queryCase,
        List<ProviderConformanceFailure> failures)
    {
        var validation = validator.Validate(queryCase.Query);
        if (!validation.IsValid)
        {
            failures.Add(new(
                subject.Name,
                queryCase.Name,
                queryCase.Area,
                $"Validation rejected supported query with failures: {FailureCodes(validation.Failures)}."));
            return;
        }

        try
        {
            var results = (await queryService.QueryAsync(queryCase.Query)).ToList();
            if (subject.RequiresNonEmptyResults && queryCase.RequiresMatch && results.Count == 0)
            {
                failures.Add(new(
                    subject.Name,
                    queryCase.Name,
                    "Fixture data",
                    "Supported query executed but returned no results from the conformance fixture."));
            }
        }
        catch (UnsupportedQueryFeatureException exception)
        {
            failures.Add(new(
                subject.Name,
                queryCase.Name,
                "Execution",
                $"Provider rejected supported query with '{exception.Code}'."));
        }
    }

    private static async Task EvaluateUnsupportedCaseAsync(
        ProviderConformanceSubject subject,
        IResourceQueryValidator validator,
        IResourceQueryService queryService,
        ProviderQueryCase queryCase,
        List<ProviderConformanceFailure> failures)
    {
        var validation = validator.Validate(queryCase.Query);
        if (validation.IsValid)
        {
            failures.Add(new(
                subject.Name,
                queryCase.Name,
                queryCase.Area,
                "Validation accepted unsupported query."));
        }

        try
        {
            _ = await queryService.QueryAsync(queryCase.Query);
            failures.Add(new(
                subject.Name,
                queryCase.Name,
                "Execution",
                "Provider accepted unsupported query during execution."));
        }
        catch (UnsupportedQueryFeatureException)
        {
        }
    }

    private static IEnumerable<ProviderQueryCase> SupportedCases(QueryCapabilityDescription capabilities)
    {
        if (capabilities.SupportedScopes.Contains(ResourceVersionScope.Latest))
            yield return new("Latest scope", "Scope", new ResourceQuery(), RequiresMatch: false);

        if (capabilities.SupportedScopes.Contains(ResourceVersionScope.Active))
        {
            yield return new(
                "Active scope",
                "Scope",
                new ResourceQuery
                {
                    Scope = ResourceVersionScope.Active,
                    ActivationChannel = capabilities.RequiresActivationChannelForActiveScope ? "Published" : null,
                    DefinitionId = "Product",
                });
        }

        if (capabilities.SupportedMetadataFields.Contains("Owner")
            && capabilities.SupportsComparison(QueryFilterType.Metadata, ComparisonOperator.Equals))
        {
            yield return new(
                "Metadata equals filter",
                "Metadata filter",
                new ResourceQuery
                {
                    Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
                });
        }

        if (capabilities.SupportedMetadataFields.Contains("Owner")
            && capabilities.SupportsComparison(QueryFilterType.Metadata, ComparisonOperator.NotEquals))
        {
            yield return new(
                "Metadata not-equals filter",
                "Metadata filter",
                new ResourceQuery
                {
                    Filter = new MetadataFilter("Owner", "bob", ComparisonOperator.NotEquals),
                });
        }

        if (capabilities.SupportedMetadataFields.Contains("Owner")
            && capabilities.SupportsComparison(QueryFilterType.Metadata, ComparisonOperator.In))
        {
            yield return new(
                "Metadata in filter",
                "Metadata filter",
                new ResourceQuery
                {
                    Filter = new MetadataFilter("Owner", new[] { "alice", "carol" }, ComparisonOperator.In),
                });
        }

        if (capabilities.SupportedMetadataFields.Contains("Owner")
            && capabilities.SupportsComparison(QueryFilterType.Metadata, ComparisonOperator.StartsWith))
        {
            yield return new(
                "Metadata starts-with filter",
                "Metadata filter",
                new ResourceQuery
                {
                    Filter = new MetadataFilter("Owner", "ali", ComparisonOperator.StartsWith),
                });
        }

        if (capabilities.SupportedFilterTypes.Contains(QueryFilterType.AspectPresence))
        {
            yield return new(
                "Aspect presence filter",
                "Aspect filter",
                new ResourceQuery
                {
                    Filter = new AspectPresenceFilter("Title"),
                });
        }

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Contains))
        {
            yield return new(
                "Facet contains filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", "Gadget", ComparisonOperator.Contains),
                });
        }

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.NotEquals))
        {
            yield return new(
                "Facet not-equals filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", "Beta Widget", ComparisonOperator.NotEquals),
                });
        }

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.In))
        {
            yield return new(
                "Facet in filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", new[] { "Alpha Gadget Updated", "Other" }, ComparisonOperator.In),
                });
        }

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.StartsWith))
        {
            yield return new(
                "Facet starts-with filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.StartsWith),
                });
        }

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Exists))
        {
            yield return new(
                "Facet exists filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", true, ComparisonOperator.Exists),
                });
        }

        if (capabilities.FacetRangeSupport.Contains(QueryValueShape.Numeric)
            && capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range))
        {
            yield return new(
                "Numeric facet range filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Price", "Amount", new RangeValue(10, 30), ComparisonOperator.Range),
                });
        }

        if (capabilities.FacetRangeSupport.Contains(QueryValueShape.DateTime)
            && capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range))
        {
            yield return new(
                "Date-like facet range filter",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter(
                        "Schedule",
                        "StartsAt",
                        new RangeValue(Utc(2026, 2, 1), Utc(2026, 2, 3)),
                        ComparisonOperator.Range),
                });
        }

        if (capabilities.SupportedLogicalOperators.Contains(LogicalOperator.And)
            && capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Equals))
        {
            yield return new(
                "Logical filter",
                "Logical filter",
                new ResourceQuery
                {
                    Filter = new LogicalExpression(LogicalOperator.And, [
                        new AspectPresenceFilter("Price"),
                        new FacetValueFilter("Inventory", "Count", 10, ComparisonOperator.Equals),
                    ]),
                });
        }

        if (capabilities.SupportsMetadataSorting && capabilities.SupportedMetadataFields.Contains("Created"))
        {
            yield return new(
                "Metadata sort",
                "Sort",
                new ResourceQuery
                {
                    DefinitionId = "Product",
                    Sorts = [new SortExpression("Created", SortDirection.Descending)],
                });
        }

        if (capabilities.SupportsFacetSorting)
        {
            yield return new(
                "Facet sort",
                "Sort",
                new ResourceQuery
                {
                    DefinitionId = "Product",
                    Sorts = [new SortExpression("Title", AspectKey: "Title")],
                });
        }

        if (capabilities.SupportsSkip && capabilities.SupportsTake)
        {
            yield return new(
                "Skip and take",
                "Paging",
                new ResourceQuery
                {
                    DefinitionId = "Product",
                    Sorts = capabilities.SupportsMetadataSorting ? [new SortExpression("Created")] : [],
                    Skip = 1,
                    Take = 1,
                });
        }
    }

    private static IEnumerable<ProviderQueryCase> UnsupportedCases(QueryCapabilityDescription capabilities)
    {
        yield return new(
            "Negative skip",
            "Paging",
            new ResourceQuery { Skip = -1 },
            RequiresMatch: false);

        yield return new(
            "Negative take",
            "Paging",
            new ResourceQuery { Take = -1 },
            RequiresMatch: false);

        if (capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.In))
        {
            yield return new(
                "Invalid facet in value shape",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.In),
                },
                RequiresMatch: false);
        }

        if (!capabilities.SupportsFacetSorting)
        {
            yield return new(
                "Unsupported facet sort",
                "Sort",
                new ResourceQuery
                {
                    Sorts = [new SortExpression("Title", AspectKey: "Title")],
                },
                RequiresMatch: false);
        }

        if (!capabilities.SupportsMetadataSorting)
        {
            yield return new(
                "Unsupported metadata sort",
                "Sort",
                new ResourceQuery
                {
                    Sorts = [new SortExpression("Created")],
                },
                RequiresMatch: false);
        }

        if (!capabilities.FacetRangeSupport.Contains(QueryValueShape.DateTime)
            && capabilities.SupportsComparison(QueryFilterType.FacetValue, ComparisonOperator.Range))
        {
            yield return new(
                "Unsupported date-like facet range",
                "Facet filter",
                new ResourceQuery
                {
                    Filter = new FacetValueFilter(
                        "Schedule",
                        "StartsAt",
                        new RangeValue(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
                        ComparisonOperator.Range),
                },
                RequiresMatch: false);
        }
    }

    private static string FailureCodes(IEnumerable<QueryValidationFailure> failures) =>
        string.Join(", ", failures.Select(static failure => failure.Code));

    private static string FailureCodes(IEnumerable<IndexProjectionFailure> failures) =>
        string.Join(", ", failures.Select(static failure => failure.Code));

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed record ProviderQueryCase(
        string Name,
        string Area,
        ResourceQuery Query,
        bool RequiresMatch = true);
}
