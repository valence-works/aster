using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.PostgresJsonb.Querying;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// PostgreSQL JSONB implementation of <see cref="IResourceQueryService"/>: each query is one parameterized
/// statement over <c>resource_versions.payload</c>.
/// </summary>
internal sealed class PostgresJsonbQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgresJsonbDataSource dataSource;
    private readonly PostgresJsonbInfrastructureRunner? infrastructure;
    private readonly IReadOnlyList<IndexProjection> indexProjections;
    private readonly ResourceQueryValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="PostgresJsonbQueryService"/>. When
    /// <see cref="PostgresJsonbAsterOptions.InitializeSchema"/> is set, pending infrastructure steps are applied
    /// before the first query.
    /// </summary>
    /// <param name="dataSource">The provider's data source.</param>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options, for the declared index projections and schema bootstrap.</param>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    public PostgresJsonbQueryService(
        PostgresJsonbDataSource dataSource,
        PostgresJsonbInfrastructureRunner infrastructure,
        PostgresJsonbAsterOptions options,
        IEnumerable<IResourceQueryCapabilitiesProvider>? capabilityProviders = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.infrastructure = options.InitializeSchema ? infrastructure : null;
        indexProjections = options.IndexProjections.ToList();
        validator = new ResourceQueryValidator(
            capabilityProviders ?? [new PostgresJsonbQueryCapabilitiesProvider(options)],
            this);
    }

    /// <inheritdoc />
    public string ProviderKey => PostgresJsonbQueryCapabilitiesProvider.ProviderKey;

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);

        var builder = new PostgresQueryBuilder(query);
        var translator = new PostgresWhereTranslator(builder.Parameters, new PostgresIndexConjuncts(builder.Parameters, indexProjections));
        if (query.Filter is not null)
        {
            translator.Validate(query.Filter);
            builder.AddPredicate(translator.Translate(query.Filter));
        }

        builder.AddSorts(query.Sorts);

        if (infrastructure is not null)
            await infrastructure.EnsureAppliedAsync(cancellationToken);

        await using var command = dataSource.Value.CreateCommand(builder.Build());
        builder.Parameters.ApplyTo(command);

        var resources = new List<Resource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resources.Add(JsonSerializer.Deserialize<Resource>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidOperationException("Unable to deserialize persisted Resource payload."));
        }

        return resources;
    }
}
