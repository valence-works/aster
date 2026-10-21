using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;
using Aster.Persistence.SqliteJson;

namespace Aster.Tests.SqliteJson;

/// <summary>
/// US4: facet metadata adds no SQLite schema: it lives in the definition's JSON payload, which is
/// unchanged when no metadata is declared. The provider-neutral checks are in FacetMetadataStoreTests.
/// </summary>
public sealed class SqliteJsonFacetMetadataSchemaTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    private SqliteJsonResourceStore CreateStore() =>
        new(new SqliteJsonAsterOptions
        {
            ConnectionString = $"Data Source={databasePath}",
        });

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private sealed class TitleAspect
    {
        public string String { get; init; } = string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T032 — SQLite persistence and schema
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterDefinitionAsync_NoMetadata_StoredPayloadContainsNoMetadataProperty()
    {
        // Arrange
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .Build();

        // Act
        await CreateStore().RegisterDefinitionAsync(definition);
        var payload = ReadDefinitionPayload("Product");

        // Assert — the persisted JSON is unchanged from today (FR-004, FR-019).
        Assert.DoesNotContain("metadata", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schema_WithMetadataPersisted_TableShapeUnchanged()
    {
        // Arrange — persist a metadata-bearing definition so the schema is fully initialized.
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(new FacetMetadata { Label = "Title" })
            .Build();
        await CreateStore().RegisterDefinitionAsync(definition);

        // Act
        var tables = SqliteJsonTestDatabase.ReadTableNames(databasePath)
            .Where(static name => !name.StartsWith("sqlite_", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var definitionColumns = ReadColumnNames("resource_definitions");

        // Assert — same tables and same resource_definitions shape as SqliteJsonSchema.cs.
        Assert.Equal(
            (IEnumerable<string>)["activation_states", "aspect_definitions", "lifecycle_markers", "resource_definitions", "resource_versions"],
            tables);
        Assert.Equal(
            (IEnumerable<string>)["tenant_id", "definition_id", "version", "id", "payload"],
            definitionColumns);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private string ReadDefinitionPayload(string definitionId)
    {
        using var connection = SqliteJsonTestDatabase.OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM resource_definitions WHERE definition_id = $definitionId;";
        command.Parameters.AddWithValue("$definitionId", definitionId);
        return (string)command.ExecuteScalar()!;
    }

    private string[] ReadColumnNames(string tableName)
    {
        using var connection = SqliteJsonTestDatabase.OpenConnection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        return [.. names];
    }
}
