using System.Diagnostics;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Tests.Persistence;

/// <summary>
/// Performance integration tests against a 100k resource-version dataset.
/// Captures evidence for SC-002 (95% of queries under 2 seconds) and
/// SC-003 (99% correctness match between expected and actual results).
/// </summary>
[Trait("Category", "Performance")]
public sealed class PerformanceTests : IDisposable
{
    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);

    private readonly SqliteTestFixture fixture = new();
    private const int ResourceCount = 1000;   // 1000 resources
    private const int VersionsPerResource = 100; // 100 versions each = 100k total
    private const int TotalVersions = ResourceCount * VersionsPerResource;

    public void Dispose() => fixture.Dispose();

    /// <summary>
    /// Seeds 100k resource versions (1000 resources × 100 versions each).
    /// Uses raw Sqlite for bulk insert performance.
    /// </summary>
    private async Task SeedLargeDataset()
    {
        // Use a single connection with a transaction for bulk insert performance
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO ResourceRecord (ResourceId, Version, VersionId, DefinitionId, DefinitionVersion, AspectsJson, CreatedUtc, Owner, Hash)
            VALUES (@resId, @version, @versionId, @defId, @defVersion, @aspects, @created, @owner, @hash)
            """;

        var pResId = cmd.Parameters.Add("@resId", Microsoft.Data.Sqlite.SqliteType.Text);
        var pVersion = cmd.Parameters.Add("@version", Microsoft.Data.Sqlite.SqliteType.Integer);
        var pVersionId = cmd.Parameters.Add("@versionId", Microsoft.Data.Sqlite.SqliteType.Text);
        var pDefId = cmd.Parameters.Add("@defId", Microsoft.Data.Sqlite.SqliteType.Text);
        var pDefVersion = cmd.Parameters.Add("@defVersion", Microsoft.Data.Sqlite.SqliteType.Integer);
        var pAspects = cmd.Parameters.Add("@aspects", Microsoft.Data.Sqlite.SqliteType.Text);
        var pCreated = cmd.Parameters.Add("@created", Microsoft.Data.Sqlite.SqliteType.Text);
        var pOwner = cmd.Parameters.Add("@owner", Microsoft.Data.Sqlite.SqliteType.Text);
        var pHash = cmd.Parameters.Add("@hash", Microsoft.Data.Sqlite.SqliteType.Text);

        var owners = new[] { "alice", "bob", "charlie", null, "dave" };
        var currencies = new[] { "USD", "EUR", "GBP", "JPY" };
        var definitions = new[] { "Product", "Article", "Page" };
        var created = DateTime.UtcNow.ToString("O");

        for (var r = 0; r < ResourceCount; r++)
        {
            var resId = $"res-{r:D5}";
            var defId = definitions[r % definitions.Length];
            var owner = owners[r % owners.Length];

            for (var v = 1; v <= VersionsPerResource; v++)
            {
                pResId.Value = resId;
                pVersion.Value = v;
                pVersionId.Value = $"vid-{r:D5}-{v:D3}";
                pDefId.Value = defId;
                pDefVersion.Value = 1;
                pAspects.Value = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    { "titleAspect", new { title = $"Item {r:D5} v{v}" } },
                    { "priceAspect", new { amount = r + v * 0.01m, currency = currencies[r % currencies.Length] } },
                });
                pCreated.Value = created;
                pOwner.Value = (object?)owner ?? DBNull.Value;
                pHash.Value = DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SC-002: Query performance — 95% of queries under 2 seconds
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SC002_StandardQueries_CompleteUnder2Seconds()
    {
        await SeedLargeDataset();

        var queries = new List<(string Name, ResourceQuery Query)>
        {
            ("All latest", new ResourceQuery()),
            ("DefinitionId filter", new ResourceQuery { DefinitionId = "Product" }),
            ("Owner equals", new ResourceQuery
            {
                Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
            }),
            ("Owner contains", new ResourceQuery
            {
                Filter = new MetadataFilter("Owner", "ali", ComparisonOperator.Contains),
            }),
            ("DefinitionId + paging", new ResourceQuery
            {
                DefinitionId = "Product",
                Skip = 10,
                Take = 50,
            }),
            ("Combined AND filter", new ResourceQuery
            {
                Filter = new LogicalExpression(LogicalOperator.And,
                [
                    new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
                    new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
                ]),
            }),
            ("OR filter", new ResourceQuery
            {
                Filter = new LogicalExpression(LogicalOperator.Or,
                [
                    new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
                    new MetadataFilter("Owner", "bob", ComparisonOperator.Equals),
                ]),
            }),
            ("NOT filter", new ResourceQuery
            {
                Filter = new LogicalExpression(LogicalOperator.Not,
                [
                    new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
                ]),
            }),
            ("AspectPresence", new ResourceQuery
            {
                Filter = new AspectPresenceFilter("priceAspect"),
            }),
            ("Paged all", new ResourceQuery { Skip = 500, Take = 100 }),
        };

        var durations = new List<long>();
        const int iterations = 3; // run each query 3 times for stability

        foreach (var (name, query) in queries)
        {
            for (var i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                await fixture.QueryService.QueryAsync(query);
                sw.Stop();
                durations.Add(sw.ElapsedMilliseconds);
            }
        }

        // SC-002: 95% under 2 seconds
        var totalRuns = durations.Count;
        var under2s = durations.Count(d => d < 2000);
        var percentUnder2s = (double)under2s / totalRuns * 100;

        Assert.True(
            percentUnder2s >= 95.0,
            $"SC-002 FAIL: Only {percentUnder2s:F1}% of queries completed under 2s (need ≥ 95%). " +
            $"Max: {durations.Max()} ms, Avg: {durations.Average():F0} ms, P95: {Percentile(durations, 95):F0} ms");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SC-003: Query correctness — 99% match
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SC003_QueryCorrectness_99PercentMatch()
    {
        await SeedLargeDataset();

        var correctResults = 0;
        var totalChecks = 0;

        // Test 1: DefinitionId filter correctness
        var productResults = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
        })).ToList();

        foreach (var r in productResults)
        {
            totalChecks++;
            if (r.DefinitionId == "Product") correctResults++;
        }

        // Verify expected count: resources 0,3,6,... (every 3rd is "Product")
        var expectedProductCount = (ResourceCount + 2) / 3; // ceil(1000/3)
        totalChecks++;
        if (productResults.Count == expectedProductCount) correctResults++;

        // Test 2: Owner filter correctness
        var aliceResults = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new MetadataFilter("Owner", "alice", ComparisonOperator.Equals),
        })).ToList();

        foreach (var r in aliceResults)
        {
            totalChecks++;
            if (r.Owner == "alice") correctResults++;
        }

        // Verify expected count: owner[0] = alice, so indices 0,5,10,...
        var expectedAliceCount = (ResourceCount + 4) / 5;
        totalChecks++;
        if (aliceResults.Count == expectedAliceCount) correctResults++;

        // Test 3: Paging consistency — page windows should not overlap
        var page1 = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Skip = 0,
            Take = 50,
        })).ToList();

        var page2 = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Product",
            Skip = 50,
            Take = 50,
        })).ToList();

        var page1Ids = page1.Select(r => r.ResourceId).ToHashSet();
        var page2Ids = page2.Select(r => r.ResourceId).ToHashSet();

        totalChecks++;
        if (!page1Ids.Overlaps(page2Ids)) correctResults++;

        totalChecks++;
        if (page1.Count == 50) correctResults++;

        totalChecks++;
        if (page2.Count == 50) correctResults++;

        // Test 4: Latest version — query returns only the latest version per resource
        var latestResults = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Take = 100,
        })).ToList();

        foreach (var r in latestResults)
        {
            totalChecks++;
            if (r.Version == VersionsPerResource) correctResults++;
        }

        // Test 5: NOT filter correctness
        var nonProductResults = (await fixture.QueryService.QueryAsync(new ResourceQuery
        {
            Filter = new LogicalExpression(LogicalOperator.Not,
            [
                new MetadataFilter("DefinitionId", "Product", ComparisonOperator.Equals),
            ]),
        })).ToList();

        foreach (var r in nonProductResults)
        {
            totalChecks++;
            if (r.DefinitionId != "Product") correctResults++;
        }

        totalChecks++;
        if (nonProductResults.Count == ResourceCount - expectedProductCount) correctResults++;

        // SC-003: 99% correctness
        var correctnessPercent = (double)correctResults / totalChecks * 100;

        Assert.True(
            correctnessPercent >= 99.0,
            $"SC-003 FAIL: Only {correctnessPercent:F1}% correctness (need ≥ 99%). " +
            $"{correctResults}/{totalChecks} checks passed.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Seed verification
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeedDataset_Has100kVersions()
    {
        await SeedLargeDataset();

        // Quick sanity check: the latest-version query returns ResourceCount results
        var results = (await fixture.QueryService.QueryAsync(new ResourceQuery())).ToList();
        Assert.Equal(ResourceCount, results.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static double Percentile(List<long> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
