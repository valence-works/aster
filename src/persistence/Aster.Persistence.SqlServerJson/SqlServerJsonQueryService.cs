using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Aster.Persistence.SqlServerJson.Querying;
using Microsoft.Data.SqlClient;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// SQL Server JSON implementation of <see cref="IResourceQueryService"/>: each query is one parameterized statement
/// over <c>resource_versions.payload</c>.
/// </summary>
internal sealed class SqlServerJsonQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string connectionString;
    private readonly SqlServerJsonInfrastructureRunner? infrastructure;
    private readonly IReadOnlyList<IndexProjection> indexProjections;
    private readonly ResourceQueryValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerJsonQueryService"/>. When
    /// <see cref="SqlServerJsonAsterOptions.InitializeSchema"/> is set, pending infrastructure steps are applied
    /// before the first query.
    /// </summary>
    /// <param name="infrastructure">The provider's infrastructure runner.</param>
    /// <param name="options">Provider options, for the connection, the declared index projections and schema bootstrap.</param>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    public SqlServerJsonQueryService(
        SqlServerJsonInfrastructureRunner infrastructure,
        SqlServerJsonAsterOptions options,
        IEnumerable<IResourceQueryCapabilitiesProvider>? capabilityProviders = null)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(options);
        connectionString = options.ConnectionString;
        this.infrastructure = options.InitializeSchema ? infrastructure : null;
        indexProjections = options.IndexProjections.ToList();
        validator = new ResourceQueryValidator(
            capabilityProviders ?? [new SqlServerJsonQueryCapabilitiesProvider(options)],
            this);
    }

    /// <inheritdoc />
    public string ProviderKey => SqlServerJsonQueryCapabilitiesProvider.ProviderKey;

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(
        ResourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);

        var builder = Translate(query, indexProjections);

        if (infrastructure is not null)
            await infrastructure.EnsureAppliedAsync(cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(builder.Build(), connection);
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

    /// <summary>
    /// The query's statement builder, with its filter and sorts translated; <see cref="SqlServerQueryBuilder.Build"/>
    /// returns the SQL.
    /// </summary>
    internal static SqlServerQueryBuilder Translate(ResourceQuery query, IReadOnlyList<IndexProjection> indexProjections)
    {
        var builder = new SqlServerQueryBuilder(query);
        var translator = new SqlServerWhereTranslator(
            builder.Parameters,
            builder.Facets,
            new SqlServerIndexConjuncts(builder.Parameters, indexProjections));
        if (query.Filter is not null)
        {
            translator.Validate(query.Filter);
            builder.AddPredicate(translator.Translate(query.Filter));
        }

        builder.AddSorts(query.Sorts);
        return builder;
    }
}
