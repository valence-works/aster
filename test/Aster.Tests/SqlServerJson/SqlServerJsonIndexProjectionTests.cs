using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqlServerJson;
using Aster.Persistence.SqlServerJson.Querying;
using Aster.Tests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqlServerJson;

/// <summary>
/// #152: declared index projections become persisted computed columns with indexes (and, for keyword arrays, indexed
/// element rows) that the infrastructure runner keeps in step with the declaration, and the queries they cover use them.
/// </summary>
public sealed class SqlServerJsonIndexProjectionTests : IDisposable
{
    private readonly SqlServerTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task Declaring_CreatesOneIndexPerProjection_Idempotently()
    {
        await using var provider = CreateProvider(PostgresTestProjections.All);
        await provider.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync();
        var runner = provider.GetRequiredService<IInfrastructureRunner>();

        // Six indexes on resource_versions, each over a computed column, and one array projection's element rows.
        Assert.Equal(PostgresTestProjections.All.Count - 1, ProjectionIndexNames().Count);
        Assert.Equal(PostgresTestProjections.All.Count - 1, ProjectionColumnNames().Count);
        Assert.Single(ArrayProjectionIds());
        Assert.Empty(await runner.ApplyAsync());
        Assert.Empty(await runner.GetPendingAsync());
        Assert.True((await runner.VerifyAsync()).IsCurrent);

        await using var again = CreateProvider(PostgresTestProjections.All);
        Assert.Empty(await again.GetRequiredService<IInfrastructureRunner>().ApplyAsync());
        Assert.Equal(PostgresTestProjections.All.Count - 1, ProjectionIndexNames().Count);
    }

    [SkippableFact]
    public async Task RemovingProjections_DropsTheirColumnsIndexesAndElementRows()
    {
        await using (var provider = CreateProvider(PostgresTestProjections.All))
        {
            await provider.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(Product(1));
            Assert.Equal(2, Scalar("SELECT COUNT(*) FROM aster_projection_elements;"));
        }

        var before = ProjectionIndexNames();
        var kept = PostgresTestProjections.All
            .Where(static projection => projection.FieldName is not ("title" or "tags"))
            .ToList();
        await using var narrowed = CreateProvider(kept, initializeSchema: false);
        var runner = narrowed.GetRequiredService<IInfrastructureRunner>();

        var pending = await runner.GetPendingAsync();
        var applied = await runner.ApplyAsync();

        var droppedIndex = Assert.Single(before.Except(ProjectionIndexNames()));
        Assert.Equal(2, pending.Count);
        Assert.Contains($"index/drop/{droppedIndex}", pending.Select(static step => step.Id));
        Assert.Equal(pending, applied);
        Assert.Equal(kept.Count, ProjectionIndexNames().Count);
        Assert.Equal(kept.Count, ProjectionColumnNames().Count);
        Assert.Empty(ArrayProjectionIds());
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM aster_projection_elements;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID(N'resource_versions');"));
        Assert.True((await runner.VerifyAsync()).IsCurrent);

        // Writes still succeed without the trigger.
        await using var writer = CreateProvider(kept);
        await writer.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(Product(2));
    }

    [SkippableFact]
    public async Task DeclaringProjections_OverExistingVersions_IndexesThem()
    {
        await using (var unindexed = database.CreateProvider())
        {
            var writer = unindexed.GetRequiredService<IResourceVersionWriter>();
            await writer.SaveVersionAsync(Product(1));
            await writer.SaveVersionAsync(Product(2));
        }

        await using var indexed = CreateProvider(PostgresTestProjections.All);
        var query = indexed.GetRequiredService<IResourceQueryService>();

        Assert.Equal(["product-2"], await QueryIdsAsync(query, new FacetValueFilter("Labels", "Tags", "TAG-2", ComparisonOperator.ArrayContains)));
        Assert.Equal(["product-1"], await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", "PRODUCT 1", ComparisonOperator.Equals)));
        Assert.Equal(["product-2"], await QueryIdsAsync(query, new FacetValueFilter("Price", "Amount", 2, ComparisonOperator.Equals)));
        Assert.Equal(4, Scalar("SELECT COUNT(*) FROM aster_projection_elements;"));
    }

    [SkippableFact]
    public async Task PruningAVersion_DeletesItsElementRows()
    {
        await using var provider = CreateProvider(PostgresTestProjections.All);
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        await writer.SaveVersionAsync(Product(1));
        await writer.SaveVersionAsync(Product(1) with { Version = 2, Id = "product-1-v2" });

        Assert.True(await provider.GetRequiredService<IResourceVersionPruningStore>().PruneVersionAsync("product-1", 1, Aster.Core.Models.Tenancy.TenantScope.Default));

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM aster_projection_elements;"));
    }

    [SkippableFact]
    public async Task ProjectedKeysTheSerializerEscapes_StillMatch()
    {
        // Paths in computed columns compare with the stored, escaped text, so they must be escaped the same way.
        IndexProjection[] projections =
        [
            IndexProjection.Facet("label", "Préis'Aspect", "Ämount\"Label", IndexFieldType.Keyword),
            IndexProjection.Facet("price", "Préis'Aspect", "Ämount", IndexFieldType.Decimal),
            IndexProjection.Facet("tags", "Préis'Aspect", "<Tags>", IndexFieldType.KeywordArray),
        ];
        await using var provider = CreateProvider(projections);
        await provider.GetRequiredService<IResourceVersionWriter>().SaveVersionAsync(Product(1) with
        {
            Aspects = new Dictionary<string, object>
            {
                ["Préis'Aspect"] = new Dictionary<string, object> { ["Ämount\"Label"] = "Grüße", ["Ämount"] = 12.5m, ["<Tags>"] = new[] { "Ünïcode" } },
            },
        });
        var query = provider.GetRequiredService<IResourceQueryService>();

        Assert.Equal(["product-1"], await QueryIdsAsync(query, new FacetValueFilter("Préis'Aspect", "Ämount\"Label", "GRÜßE", ComparisonOperator.Equals)));
        Assert.Equal(["product-1"], await QueryIdsAsync(query, new FacetValueFilter("Préis'Aspect", "Ämount", 12.5, ComparisonOperator.Equals)));
        Assert.Equal(["product-1"], await QueryIdsAsync(query, new FacetValueFilter("Préis'Aspect", "<Tags>", "üNÏCODE", ComparisonOperator.ArrayContains)));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongValuesAndTrailingSpaces_MatchExactly(bool indexed)
    {
        // Keys hold a 400-character prefix and JSON_VALUE stops at 4,000 characters; neither may change a result.
        var shared = new string('x', 450);
        var huge = new string('y', 5000);
        await using var provider = indexed ? CreateProvider(PostgresTestProjections.All) : database.CreateProvider();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        foreach (var (number, value) in new[] { (1, shared + "-one"), (2, shared + "-two"), (3, huge), (4, "Alpha ") })
        {
            await writer.SaveVersionAsync(Product(number) with
            {
                Aspects = new Dictionary<string, object>
                {
                    ["Title"] = new { Title = value },
                    ["Labels"] = new { Tags = new[] { value } },
                },
            });
        }

        var query = provider.GetRequiredService<IResourceQueryService>();

        Assert.Equal(["product-1"], await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", (shared + "-ONE").ToUpperInvariant(), ComparisonOperator.Equals)));
        Assert.Equal(["product-3"], await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", huge.ToUpperInvariant(), ComparisonOperator.Equals)));
        Assert.Equal(["product-2"], await QueryIdsAsync(query, new FacetValueFilter("Labels", "Tags", shared + "-two", ComparisonOperator.ArrayContains)));
        Assert.Equal(["product-3"], await QueryIdsAsync(query, new FacetValueFilter("Labels", "Tags", huge, ComparisonOperator.ArrayContains)));
        Assert.Empty(await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", shared, ComparisonOperator.Equals)));
        Assert.Empty(await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", "alpha", ComparisonOperator.Equals)));
        Assert.Empty(await QueryIdsAsync(query, new FacetValueFilter("Labels", "Tags", "ALPHA", ComparisonOperator.ArrayContains)));
        Assert.Equal(["product-4"], await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", "ALPHA ", ComparisonOperator.Equals)));
        Assert.Equal(["product-4"], await QueryIdsAsync(query, new FacetValueFilter("Title", "Title", "alpha", ComparisonOperator.StartsWith)));
    }

    [SkippableFact]
    public void InvalidProjections_FailAtRegistration()
    {
        var services = new ServiceCollection().AddAsterCore();

        var exception = Assert.Throws<ArgumentException>(() => services.AddAsterSqlServerJson(options =>
        {
            options.ConnectionString = "Server=unused";
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
            await provider.GetRequiredService<IResourceDefinitionStore>().ListDefinitionsAsync();

        // 2,000 varied products, with fresh statistics, so a selective predicate's index is the cheapest path.
        Execute("""
            WITH numbers AS (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO resource_versions (tenant_id, resource_id, version, id, definition_id, created, owner, payload)
            SELECT N'default', CONCAT(N'product-', n), 1, CONCAT(N'product-', n, N'-v1'), N'Product', SYSUTCDATETIME(), CONCAT(N'owner-', n),
                CONCAT(
                    N'{"aspects":{"Title":{"title":"Product ', n, N'"},"Category":{"category":"Category ', n % 100,
                    N'"},"Price":{"amount":', n, N'},"Inventory":{"count":', n,
                    N'},"Schedule":{"startsAt":"', CONVERT(nvarchar(19), DATEADD(day, n, DATETIME2FROMPARTS(2020, 1, 1, 0, 0, 0, 0, 0)), 126),
                    N'Z"},"Labels":{"tags":["tag-', n, N'","tag-', n % 7, N'"]}}}')
            FROM numbers;
            UPDATE STATISTICS resource_versions WITH FULLSCAN;
            UPDATE STATISTICS aster_projection_elements WITH FULLSCAN;
            """);

        var projection = SqlServerProjections.For(PostgresTestProjections.All.Single(projection => projection.FieldName == fieldName));
        var indexName = projection.Kind == SqlServerProjectionKind.ArrayElements ? "ix_aster_projection_elements_key" : projection.Name;

        var plan = await ShowPlanAsync(new ResourceQuery { Filter = filter });

        Assert.True(plan.Contains($"[{indexName}]", StringComparison.Ordinal), plan);
    }

    [SkippableFact]
    public void NegatedPredicates_DoNotGetAnIndexConjunct()
    {
        var positive = SqlServerJsonQueryService.Translate(
            new ResourceQuery { Filter = new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.Equals) },
            PostgresTestProjections.All).Build();
        var negated = SqlServerJsonQueryService.Translate(
            new ResourceQuery
            {
                Filter = new LogicalExpression(LogicalOperator.Not, [new FacetValueFilter("Title", "Title", "Alpha", ComparisonOperator.Equals)]),
            },
            PostgresTestProjections.All).Build();

        Assert.Contains(SqlServerProjections.ColumnPrefix, positive);
        Assert.DoesNotContain(SqlServerProjections.ColumnPrefix, negated);
    }

    private static Resource Product(int number) => new()
    {
        ResourceId = $"product-{number}",
        Id = $"product-{number}-v1",
        DefinitionId = "Product",
        Version = 1,
        Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Owner = $"owner-{number}",
        Aspects = new Dictionary<string, object>
        {
            ["Title"] = new { Title = $"Product {number}" },
            ["Price"] = new { Amount = number },
            ["Labels"] = new { Tags = new[] { $"tag-{number}", "shared" } },
        },
    };

    private static async Task<List<string>> QueryIdsAsync(IResourceQueryService query, FilterExpression filter) =>
        (await query.QueryAsync(new ResourceQuery { Filter = filter })).Select(static resource => resource.ResourceId).ToList();

    private ServiceProvider CreateProvider(IEnumerable<IndexProjection> projections, bool initializeSchema = true) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqlServerJson(options =>
            {
                options.ConnectionString = database.ConnectionString;
                options.InitializeSchema = initializeSchema;
                foreach (var projection in projections)
                    options.IndexProjections.Add(projection);
            })
            .BuildServiceProvider();

    // The query service's SQL, run with its actual plan returned after the results: the plan names the indexes read.
    private async Task<string> ShowPlanAsync(ResourceQuery query)
    {
        var builder = SqlServerJsonQueryService.Translate(query, PostgresTestProjections.All);
        var sql = builder.Build();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var on = new SqlCommand("SET STATISTICS XML ON;", connection))
            await on.ExecuteNonQueryAsync();

        await using var command = new SqlCommand(sql, connection);
        builder.Parameters.ApplyTo(command);
        await using var reader = await command.ExecuteReaderAsync();
        var plans = new List<string>();
        do
        {
            if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.Ordinal))
            {
                while (await reader.ReadAsync())
                    plans.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync());

        return string.Join(Environment.NewLine, plans);
    }

    private void Execute(string sql) => Scalar(sql);

    private object? Scalar(string sql)
    {
        using var connection = new SqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    private List<string> ProjectionIndexNames() => Names(
        $"SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID(N'resource_versions') AND name LIKE N'{SqlServerProjections.NamePrefix.Replace("_", "[_]", StringComparison.Ordinal)}%';");

    private List<string> ProjectionColumnNames() => Names(
        $"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'resource_versions') AND name LIKE N'{SqlServerProjections.ColumnPrefix.Replace("_", "[_]", StringComparison.Ordinal)}%';");

    private List<string> ArrayProjectionIds() => Names("SELECT projection_id FROM aster_array_projections;");

    private List<string> Names(string sql)
    {
        using var connection = new SqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names.Order(StringComparer.Ordinal).ToList();
    }
}
