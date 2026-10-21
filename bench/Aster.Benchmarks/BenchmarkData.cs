using System.Text;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Benchmarks;

/// <summary>
/// Builds a provider and seeds a realistic data set: <c>Author</c> resources, and <c>Article</c> resources
/// with <see cref="VersionsPerArticle"/> versions each (about 3 KB of aspect payload per version), so the
/// total number of resource versions is the requested size. Every other article has its latest version
/// active in the <c>published</c> channel.
/// </summary>
internal sealed class BenchmarkData : IAsyncDisposable
{
    public const int VersionsPerArticle = 4;
    public const int Authors = 100;
    public const string Published = "published";

    private static readonly string Body = BuildBody();

    private readonly string? databasePath;

    private BenchmarkData(ServiceProvider services, string? databasePath, int articleCount)
    {
        Services = services;
        this.databasePath = databasePath;
        ArticleCount = articleCount;
    }

    public ServiceProvider Services { get; }

    public int ArticleCount { get; }

    public IResourceManager Manager => Services.GetRequiredService<IResourceManager>();

    public static string ArticleId(int index) => $"article-{index:D7}";

    public static string AuthorId(int index) => $"author-{index:D3}";

    public static async Task<BenchmarkData> CreateAsync(ProviderKind provider, int totalVersions)
    {
        // The data set is the authors plus whole articles; refuse totals it can't hit exactly.
        if (totalVersions < Authors + VersionsPerArticle || (totalVersions - Authors) % VersionsPerArticle != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalVersions),
                totalVersions,
                $"Total versions must be {Authors} plus a positive multiple of {VersionsPerArticle} (for example 10000, 100000 or 1000000).");
        }

        var services = new ServiceCollection().AddAsterCore();
        string? databasePath = null;
        if (provider == ProviderKind.Sqlite)
        {
            databasePath = Path.Combine(Path.GetTempPath(), $"aster-bench-{Guid.NewGuid():N}.db");
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        }

        var data = new BenchmarkData(services.BuildServiceProvider(), databasePath, (totalVersions - Authors) / VersionsPerArticle);
        await data.SeedAsync();
        if (databasePath is not null)
            CheckpointWal(databasePath);
        return data;
    }

    /// <summary>
    /// Folds the seeding writes into the main database file, so measured operations don't pay for
    /// checkpointing a write-ahead log that seeding left behind.
    /// </summary>
    public static void CheckpointWal(string databasePath)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    public static Dictionary<string, object> ArticleAspects(int index, int version) => new()
    {
        ["Content"] = new Dictionary<string, object>
        {
            ["Title"] = $"Article {index} revision {version}",
            ["Status"] = index % 3 == 0 ? "draft" : "published",
            ["Priority"] = index % 10,
            ["Tags"] = new[] { $"tag-{index % 50}", $"tag-{(index + 7) % 50}", "aster" },
            ["Body"] = Body,
        },
        ["Links"] = new Dictionary<string, object> { ["Author"] = AuthorId(index % Authors) },
    };

    private async Task SeedAsync()
    {
        var definitions = Services.GetRequiredService<IResourceDefinitionStore>();
        await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Author")
            .WithAspect("Profile").WithFacet("Name", "string", isRequired: true)
            .Build());
        await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Article")
            .WithAspect("Content")
                .WithFacet("Title", "string", isRequired: true)
                .WithFacet("Status", "string")
                .WithFacet("Priority", "int")
                .WithFacet("Tags", "string[]")
                .WithFacet("Body", "string")
            .WithAspect("Links")
                .WithFacet("Author", "reference")
                .WithFacetMetadata(new FacetMetadata { TargetDefinitionIds = ["Author"] })
            .Build());

        // Seed through the version writer: it skips per-save validation and reads, so setup measures nothing.
        var writer = Services.GetRequiredService<IResourceVersionWriter>();
        var created = DateTime.UtcNow.AddDays(-30);
        for (var author = 0; author < Authors; author++)
        {
            await writer.SaveVersionAsync(new Resource
            {
                ResourceId = AuthorId(author),
                Id = Guid.NewGuid().ToString(),
                DefinitionId = "Author",
                DefinitionVersion = 1,
                Version = 1,
                Created = created,
                Aspects = new Dictionary<string, object> { ["Profile"] = new Dictionary<string, object> { ["Name"] = $"Author {author}" } },
            });
        }

        for (var article = 0; article < ArticleCount; article++)
        {
            for (var version = 1; version <= VersionsPerArticle; version++)
            {
                await writer.SaveVersionAsync(new Resource
                {
                    ResourceId = ArticleId(article),
                    Id = Guid.NewGuid().ToString(),
                    DefinitionId = "Article",
                    DefinitionVersion = 1,
                    Version = version,
                    Created = created.AddMinutes(version),
                    Aspects = ArticleAspects(article, version),
                });
            }

            if (article % 2 == 0)
                await Manager.ActivateAsync(ArticleId(article), VersionsPerArticle, Published);
        }
    }

    private static string BuildBody()
    {
        var builder = new StringBuilder();
        while (builder.Length < 3_000)
            builder.Append("Aster keeps versioned control-plane resources with aspects, facets and activation channels. ");

        return builder.ToString(0, 3_000);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (databasePath is not null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}

/// <summary>The provider a benchmark runs against.</summary>
public enum ProviderKind
{
    /// <summary>The in-memory provider from <c>AddAsterCore</c>.</summary>
    InMemory,

    /// <summary>The SQLite JSON provider, on a temporary file database.</summary>
    Sqlite,
}
