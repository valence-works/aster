using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.PostgresJsonb;
using Aster.Persistence.PostgresJsonb.Querying;
using Aster.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aster.Tests.PostgresJsonb;

/// <summary>
/// #100: declared index projections become indexes the infrastructure runner keeps in step with the declaration,
/// and the queries they cover use them.
/// </summary>
public sealed class PostgresJsonbIndexProjectionTests : IDisposable
{
    private readonly PostgresTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task Declaring_CreatesOneIndexPerProjection_Idempotently()
    {
        await using var provider = CreateProvider(PostgresTestProjections.All);
        _ = provider.GetRequiredService<IResourceDefinitionStore>();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        Assert.Equal(PostgresTestProjections.All.Count, ProjectionIndexNames().Count);
        Assert.Empty(await runner.ApplyAsync());
        Assert.Empty(await runner.GetPendingAsync());
        Assert.True((await runner.VerifyAsync()).IsCurrent);

        await using var again = CreateProvider(PostgresTestProjections.All);
        Assert.Empty(await again.GetRequiredService<IInfrastructureRunner>().ApplyAsync());
        Assert.Equal(PostgresTestProjections.All.Count, ProjectionIndexNames().Count);
    }

    [SkippableFact]
    public async Task RemovingAProjection_DropsItsIndex()
    {
        await using (var provider = CreateProvider(PostgresTestProjections.All))
            _ = provider.GetRequiredService<IResourceDefinitionStore>();

        var before = ProjectionIndexNames();
        var kept = PostgresTestProjections.All.Skip(1).ToList();
        await using var narrowed = CreateProvider(kept, initializeSchema: false);
        var runner = narrowed.GetRequiredService<IInfrastructureRunner>();

        var pending = await runner.GetPendingAsync();
        var applied = await runner.ApplyAsync();

        var dropped = Assert.Single(before.Except(ProjectionIndexNames()));
        Assert.Equal([$"index/drop/{dropped}"], pending.Select(static step => step.Id));
        Assert.Equal(pending, applied);
        Assert.Equal(kept.Count, ProjectionIndexNames().Count);
        Assert.True((await runner.VerifyAsync()).IsCurrent);
    }

    [SkippableFact]
    public void InvalidProjections_FailAtRegistration()
    {
        var services = new ServiceCollection().AddAsterCore();

        var exception = Assert.Throws<ArgumentException>(() => services.AddAsterPostgresJsonb(options =>
        {
            options.ConnectionString = "Host=unused";
            options.IndexProjections.Add(IndexProjection.Facet("tags", "Labels", "Tags", IndexFieldType.Keyword, isMultiValue: true));
        }));

        Assert.Contains(IndexProjectionFailureCodes.InvalidProjectionDeclaration, exception.Message);
    }

    public static TheoryData<string, FilterExpression> IndexedFilters() => new()
    {
        { "title", new FacetValueFilter("Title", "Title", "product 42", ComparisonOperator.Equals) },
        { "category", new FacetValueFilter("Category", "Category", new[] { "Category 1", "Category 2" }, ComparisonOperator.In) },
        { "amount", new FacetValueFilter("Price", "Amount", 20, ComparisonOperator.Equals) },
        { "amount", new FacetValueFilter("Price", "Amount", new RangeValue(10, 30), ComparisonOperator.Range) },
        { "count", new FacetValueFilter("Inventory", "Count", new[] { 5, 10 }, ComparisonOperator.In) },
        { "startsAt", new FacetValueFilter("Schedule", "StartsAt", new RangeValue(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc)), ComparisonOperator.Range) },
        { "tags", new FacetValueFilter("Labels", "Tags", "TAG-42", ComparisonOperator.ArrayContains) },
        { "tags", new FacetValueFilter("Labels", "Tags", new[] { "tag-42", "tag-43" }, ComparisonOperator.ArrayContainsAny) },
        { "tags", new FacetValueFilter("Labels", "Tags", new[] { "tag-42", "tag-0" }, ComparisonOperator.ArrayContainsAll) },
        { "owner", new MetadataFilter("Owner", "OWNER-42", ComparisonOperator.Equals) },
        { "title", new LogicalExpression(LogicalOperator.And, [
            new AspectPresenceFilter("Price"),
            new FacetValueFilter("Title", "Title", "Product 42", ComparisonOperator.Equals),
        ]) },
    };

    [SkippableTheory]
    [MemberData(nameof(IndexedFilters))]
    public async Task IndexedQueries_UseTheProjectionIndex(string fieldName, FilterExpression filter)
    {
        await using (var provider = CreateProvider(PostgresTestProjections.All))
            _ = provider.GetRequiredService<IResourceDefinitionStore>();

        // 2,000 varied products, analyzed, so a selective predicate's index is the cheapest path.
        Execute("""
            INSERT INTO resource_versions (tenant_id, resource_id, version, id, definition_id, created, owner, payload)
            SELECT 'default', 'product-' || n, 1, 'product-' || n || '-v1', 'Product', now(), 'owner-' || n,
                jsonb_build_object('aspects', jsonb_build_object(
                    'Title', jsonb_build_object('title', 'Product ' || n),
                    'Category', jsonb_build_object('category', 'Category ' || (n % 100)),
                    'Price', jsonb_build_object('amount', n),
                    'Inventory', jsonb_build_object('count', n),
                    'Schedule', jsonb_build_object('startsAt', to_char(timestamp '2020-01-01' + n * interval '1 day', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')),
                    'Labels', jsonb_build_object('tags', jsonb_build_array('tag-' || n, 'tag-' || (n % 7)))))
            FROM generate_series(1, 2000) n;
            ANALYZE resource_versions;
            """);

        var projection = PostgresTestProjections.All.Single(projection => projection.FieldName == fieldName);
        var indexName = PostgresProjectionIndexes.Plan([projection]).Single().Name;

        var plan = await ExplainAsync(new ResourceQuery { Filter = filter });

        Assert.True(plan.Contains(indexName, StringComparison.Ordinal), plan);
    }

    [SkippableFact]
    public async Task NegatedPredicates_DoNotGetAnIndexConjunct()
    {
        var parameters = new PostgresParameterBag();
        var translator = new PostgresWhereTranslator(parameters, new PostgresIndexConjuncts(parameters, PostgresTestProjections.All));

        var positive = translator.Translate(new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.Equals));
        var negated = translator.Translate(new LogicalExpression(LogicalOperator.Not, [
            new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.Equals),
        ]));

        Assert.Contains(" IN (", positive);
        Assert.DoesNotContain(" IN (", negated);
    }

    private ServiceProvider CreateProvider(IEnumerable<IndexProjection> projections, bool initializeSchema = true) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterPostgresJsonb(options =>
            {
                options.ConnectionString = database.ConnectionString;
                options.InitializeSchema = initializeSchema;
                foreach (var projection in projections)
                    options.IndexProjections.Add(projection);
            })
            .BuildServiceProvider();

    // The query service's SQL, explained with sequential scans disabled so a usable index always wins: the test asks
    // whether the index matches the query, not whether 50 rows are worth an index.
    private async Task<string> ExplainAsync(ResourceQuery query)
    {
        var builder = new PostgresQueryBuilder(query);
        var translator = new PostgresWhereTranslator(builder.Parameters, new PostgresIndexConjuncts(builder.Parameters, PostgresTestProjections.All));
        builder.AddPredicate(translator.Translate(query.Filter!));
        var sql = builder.Build();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off;", connection))
            await off.ExecuteNonQueryAsync();

        await using var command = new NpgsqlCommand($"EXPLAIN {sql}", connection);
        builder.Parameters.ApplyTo(command);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));

        return string.Join(Environment.NewLine, lines);
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private List<string> ProjectionIndexNames()
    {
        using var connection = new NpgsqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE tablename = 'resource_versions' AND starts_with(indexname, @prefix) ORDER BY indexname;",
            connection);
        command.Parameters.AddWithValue("prefix", PostgresProjectionIndexes.NamePrefix);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }
}
