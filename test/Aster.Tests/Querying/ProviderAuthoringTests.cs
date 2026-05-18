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

public sealed class ProviderAuthoringTests
{
    [Fact]
    public void AddResourceQueryProvider_RegistersCustomProviderAsActiveQueryProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var queryService = Assert.IsType<CustomQueryService>(provider.GetRequiredService<IResourceQueryService>());
        var identity = provider.GetRequiredService<IResourceQueryProviderIdentity>();
        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;

        Assert.Same(queryService, identity);
        Assert.Equal(CustomQueryService.ProviderKeyValue, identity.ProviderKey);
        Assert.Equal(CustomQueryService.ProviderKeyValue, capabilities.ProviderKey);
    }

    [Fact]
    public void AddResourceQueryProvider_RegistersConcreteTypesAndSharedInterfacesAsSingletons()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var queryService = provider.GetRequiredService<CustomQueryService>();
        var activeQueryService = provider.GetRequiredService<IResourceQueryService>();
        var identity = provider.GetRequiredService<IResourceQueryProviderIdentity>();
        var capabilitiesProvider = provider.GetRequiredService<CustomCapabilitiesProvider>();
        var activeCapabilitiesProvider = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>();

        Assert.Same(queryService, activeQueryService);
        Assert.Same(queryService, provider.GetRequiredService<IResourceQueryService>());
        Assert.Same(queryService, identity);
        Assert.Same(capabilitiesProvider, activeCapabilitiesProvider);
        Assert.Same(capabilitiesProvider, provider.GetRequiredService<IResourceQueryCapabilitiesProvider>());
    }

    [Fact]
    public void AddResourceQueryProvider_UsesCustomCapabilitiesInsteadOfDefaults()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var validator = provider.GetRequiredService<IResourceQueryValidator>();
        var facetSortResult = validator.Validate(new ResourceQuery
        {
            Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
        });

        Assert.False(facetSortResult.IsValid);
        Assert.Contains(facetSortResult.Failures, failure => failure.Code == "unsupported-facet-sort");
        Assert.True(validator.Validate(new ResourceQuery()).IsValid);
    }

    [Fact]
    public void AddResourceQueryProvider_ExposesCustomIndexProjectionDeclarations()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;

        Assert.Collection(
            capabilities.IndexProjections,
            projection =>
            {
                Assert.Equal("resource_id", projection.FieldName);
                Assert.Equal(IndexProjectionSourceKind.Metadata, projection.Source.Kind);
                Assert.Equal("ResourceId", projection.Source.MetadataField);
                Assert.Equal(IndexFieldType.Keyword, projection.FieldType);
            },
            projection =>
            {
                Assert.Equal("title", projection.FieldName);
                Assert.Equal(IndexProjectionSourceKind.Facet, projection.Source.Kind);
                Assert.Equal("Title", projection.Source.AspectKey);
                Assert.Equal("Title", projection.Source.FacetKey);
                Assert.Equal(IndexFieldType.NormalizedText, projection.FieldType);
            });
    }

    [Fact]
    public void Validate_WithMissingCustomCapabilities_FailsClosedWithProviderKey()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<CustomQueryService>()
            .AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<CustomQueryService>())
            .AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<CustomQueryService>())
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains(CustomQueryService.ProviderKeyValue, failure.Message);
        Assert.Contains("matching ProviderKey", failure.Message);
    }

    [Fact]
    public void Validate_WithMismatchedCustomCapabilities_FailsClosedWithProviderKey()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, MismatchedCapabilitiesProvider>()
            .BuildServiceProvider();

        var result = provider.GetRequiredService<IResourceQueryValidator>().Validate(new ResourceQuery());

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("capabilities-not-declared", failure.Code);
        Assert.Contains(CustomQueryService.ProviderKeyValue, failure.Message);
        Assert.Contains("matching ProviderKey", failure.Message);
    }

    [Fact]
    public async Task CustomProviderExecution_CanRunSharedValidationBeforeExecution()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<ValidatingCustomQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var queryService = provider.GetRequiredService<IResourceQueryService>();

        await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(() =>
            queryService.QueryAsync(new ResourceQuery
            {
                Sorts = [new SortExpression("Title", AspectKey: "TitleAspect")],
            }).AsTask());
        Assert.Empty(await queryService.QueryAsync(new ResourceQuery()));
    }

    [Fact]
    public async Task CustomProviderExecution_CanRaiseProviderSpecificStructuredFailures()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<ProviderSpecificRejectingQueryService, CustomCapabilitiesProvider>()
            .BuildServiceProvider();

        var queryService = provider.GetRequiredService<IResourceQueryService>();

        var exception = await Assert.ThrowsAsync<UnsupportedQueryFeatureException>(() =>
            queryService.QueryAsync(new ResourceQuery()).AsTask());

        Assert.Equal("unsupported-custom-value", exception.Code);
        Assert.Equal("value shape", exception.Feature);
        Assert.Equal("Filter.Value", exception.Path);
        Assert.Contains("custom value shape", exception.Message);
    }

    [Fact]
    public void BuiltInProviderRegistrations_RemainCompatible()
    {
        using var inMemoryProvider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        using var sqliteProvider = new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

        var inMemoryCapabilities = inMemoryProvider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;
        var sqliteCapabilities = sqliteProvider.GetRequiredService<IResourceQueryCapabilitiesProvider>().Capabilities;

        Assert.Equal(InMemoryQueryCapabilitiesProvider.ProviderKey, inMemoryCapabilities.ProviderKey);
        Assert.Equal(SqliteJsonQueryCapabilitiesProvider.ProviderKey, sqliteCapabilities.ProviderKey);
        Assert.Equal(
            InMemoryQueryCapabilitiesProvider.ProviderKey,
            inMemoryProvider.GetRequiredService<IResourceQueryProviderIdentity>().ProviderKey);
        Assert.Equal(
            SqliteJsonQueryCapabilitiesProvider.ProviderKey,
            sqliteProvider.GetRequiredService<IResourceQueryProviderIdentity>().ProviderKey);
        Assert.True(inMemoryCapabilities.SupportsFacetSorting);
        Assert.True(sqliteCapabilities.SupportsFacetSorting);
    }

    [Fact]
    public void AddAsterSqliteJson_ReplacesCustomProviderIdentityWhenRegisteredAfterCustomProvider()
    {
        using var provider = new ServiceCollection()
            .AddAsterCore()
            .AddResourceQueryProvider<CustomQueryService, CustomCapabilitiesProvider>()
            .AddAsterSqliteJson(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.InitializeSchema = false;
            })
            .BuildServiceProvider();

        Assert.IsType<SqliteJsonQueryService>(provider.GetRequiredService<IResourceQueryService>());
        Assert.Equal(
            SqliteJsonQueryCapabilitiesProvider.ProviderKey,
            provider.GetRequiredService<IResourceQueryProviderIdentity>().ProviderKey);
    }

    [Fact]
    public void AddAsterSqliteJson_ResolvesProviderIdentityWithoutInitializingSqlite()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            using var provider = new ServiceCollection()
                .AddAsterCore()
                .AddAsterSqliteJson(options =>
                {
                    options.ConnectionString = $"Data Source={databasePath}";
                })
                .BuildServiceProvider();

            var identity = provider.GetRequiredService<IResourceQueryProviderIdentity>();

            Assert.Equal(SqliteJsonQueryCapabilitiesProvider.ProviderKey, identity.ProviderKey);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class CustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public const string ProviderKeyValue = "custom-provider";

        public string ProviderKey => ProviderKeyValue;

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default) =>
            new([]);
    }

    private sealed class ValidatingCustomQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        private readonly ResourceQueryValidator validator;

        public ValidatingCustomQueryService(IEnumerable<IResourceQueryCapabilitiesProvider> capabilityProviders)
        {
            validator = new(capabilityProviders, this);
        }

        public string ProviderKey => CustomQueryService.ProviderKeyValue;

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

    private sealed class ProviderSpecificRejectingQueryService : IResourceQueryService, IResourceQueryProviderIdentity
    {
        public string ProviderKey => CustomQueryService.ProviderKeyValue;

        public ValueTask<IEnumerable<Resource>> QueryAsync(
            ResourceQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new UnsupportedQueryFeatureException(
                code: "unsupported-custom-value",
                feature: "value shape",
                message: "This provider cannot execute the supplied custom value shape.",
                path: "Filter.Value");
        }
    }

    private sealed class CustomCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = CreateCapabilities(CustomQueryService.ProviderKeyValue);
    }

    private sealed class MismatchedCapabilitiesProvider : IResourceQueryCapabilitiesProvider
    {
        public QueryCapabilityDescription Capabilities { get; } = CreateCapabilities("other-provider");
    }

    private static QueryCapabilityDescription CreateCapabilities(string providerKey) =>
        new(
            ProviderKey: providerKey,
            ProviderName: "Custom Provider",
            SupportedScopes: new HashSet<ResourceVersionScope> { ResourceVersionScope.Latest },
            RequiresActivationChannelForActiveScope: false,
            SupportedFilterTypes: new HashSet<QueryFilterType>(),
            SupportedLogicalOperators: new HashSet<LogicalOperator>(),
            SupportedComparisonOperators: new Dictionary<QueryFilterType, IReadOnlySet<ComparisonOperator>>(),
            SupportedMetadataFields: new HashSet<string>(),
            MetadataContainsFields: new HashSet<string>(),
            SupportsMetadataSorting: false,
            SupportsFacetSorting: false,
            SupportsSkip: false,
            SupportsTake: false,
            FacetRangeSupport: new HashSet<QueryValueShape>(),
            UnsupportedFeatures: [],
            IndexProjections:
            [
                IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
                IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
            ]);
}
