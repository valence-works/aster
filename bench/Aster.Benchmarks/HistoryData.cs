using System.Text;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Persistence.SqliteJson;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Benchmarks;

/// <summary>
/// Long-history data for #105: <see cref="Resources"/> <c>Page</c> resources with a given number of versions each.
/// Each version is a full snapshot of about 3 KB, edited like a typical control-plane resource:
/// <list type="bullet">
/// <item>the title changes on every version;</item>
/// <item>the status flips every 5th version;</item>
/// <item>one sentence of the body changes every 10th version;</item>
/// <item>the owner changes every 50th version.</item>
/// </list>
/// </summary>
internal sealed class HistoryData : IAsyncDisposable
{
    public const int Resources = 20;

    private static readonly string[] Sentences = Enumerable.Range(0, 30)
        .Select(static index => $"Sentence {index:D2} explains how Aster keeps versioned resources with aspects and channels. ")
        .ToArray();

    private readonly string? databasePath;

    private HistoryData(ServiceProvider services, string? databasePath)
    {
        Services = services;
        this.databasePath = databasePath;
    }

    public ServiceProvider Services { get; }

    public IResourceManager Manager => Services.GetRequiredService<IResourceManager>();

    public string? DatabasePath => databasePath;

    public static string ResourceId(int index) => $"page-{index:D3}";

    /// <summary>
    /// History lengths to benchmark: <c>ASTER_HISTORY_VERSIONS</c> (comma-separated), defaulting to 100 and 1,000.
    /// </summary>
    public static IEnumerable<int> HistoryLengths() =>
        (Environment.GetEnvironmentVariable("ASTER_HISTORY_VERSIONS") is { Length: > 0 } configured ? configured : "100,1000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse);

    public static Dictionary<string, object> Aspects(int version) => new()
    {
        ["Content"] = new Dictionary<string, object>
        {
            ["Title"] = $"Page revision {version}",
            ["Status"] = version / 5 % 2 == 0 ? "draft" : "published",
            ["Body"] = Body(version / 10),
        },
        ["Ownership"] = new Dictionary<string, object> { ["Owner"] = $"team-{version / 50 % 7}" },
    };

    private static string Body(int revision)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < Sentences.Length; index++)
            builder.Append(index == revision % Sentences.Length ? $"Sentence {index:D2} was rewritten in revision {revision}. " : Sentences[index]);

        return builder.ToString();
    }

    public static async Task<HistoryData> CreateAsync(ProviderKind provider, int versionsPerResource)
    {
        var services = new ServiceCollection().AddAsterCore();
        string? databasePath = null;
        if (provider == ProviderKind.Sqlite)
        {
            databasePath = Path.Combine(Path.GetTempPath(), $"aster-history-{Guid.NewGuid():N}.db");
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");
        }

        var data = new HistoryData(services.BuildServiceProvider(), databasePath);
        await data.SeedAsync(versionsPerResource);
        if (databasePath is not null)
            BenchmarkData.CheckpointWal(databasePath);
        return data;
    }

    private async Task SeedAsync(int versionsPerResource)
    {
        await Services.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Page")
            .WithAspect("Content")
                .WithFacet("Title", "string", isRequired: true)
                .WithFacet("Status", "string")
                .WithFacet("Body", "string")
            .WithAspect("Ownership")
                .WithFacet("Owner", "string")
            .Build());

        var writer = Services.GetRequiredService<IResourceVersionWriter>();
        var created = DateTime.UtcNow.AddDays(-365);
        for (var resource = 0; resource < Resources; resource++)
        {
            for (var version = 1; version <= versionsPerResource; version++)
            {
                await writer.SaveVersionAsync(new Resource
                {
                    ResourceId = ResourceId(resource),
                    Id = Guid.NewGuid().ToString(),
                    DefinitionId = "Page",
                    DefinitionVersion = 1,
                    Version = version,
                    Created = created.AddMinutes(version),
                    Aspects = Aspects(version),
                });
            }
        }
    }

    /// <summary>
    /// Prints, per history length, the SQLite bytes per stored version and the payload a delta scheme would store
    /// instead: whole changed aspects, or only changed facets.
    /// </summary>
    public static async Task WriteStorageReportAsync(TextWriter output, IEnumerable<int> historyLengths)
    {
        output.WriteLine("| Versions per resource | Stored versions | SQLite file | Bytes per version | Snapshot payload | Changed-aspect delta | Changed-facet delta | Est. bytes per version with facet deltas | Est. on-disk saving |");
        output.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var length in historyLengths)
        {
            await using var data = await CreateAsync(ProviderKind.Sqlite, length);
            var fileBytes = Checkpoint(data.DatabasePath!);
            var stored = Resources * length;

            long snapshot = 0, aspectDelta = 0, facetDelta = 0;
            for (var version = 1; version <= length; version++)
            {
                var current = Aspects(version);
                snapshot += Size(current);
                if (version == 1)
                {
                    aspectDelta += Size(current);
                    facetDelta += Size(current);
                    continue;
                }

                var previous = Aspects(version - 1);
                foreach (var (key, aspect) in current)
                {
                    var now = (Dictionary<string, object>)aspect;
                    var before = (Dictionary<string, object>)previous[key];
                    var changed = now.Where(pair => !Equals(pair.Value, before.GetValueOrDefault(pair.Key))).ToDictionary();
                    if (changed.Count > 0)
                    {
                        aspectDelta += Size(now);
                        facetDelta += Size(new Dictionary<string, object> { [key] = changed });
                    }
                }
            }

            // A delta row keeps the per-row overhead (envelope, keys, page and index space) and swaps only
            // the snapshot payload for the delta payload.
            var bytesPerVersion = fileBytes / stored;
            var withFacetDeltas = bytesPerVersion - snapshot / length + facetDelta / length;
            output.WriteLine(
                $"| {length:N0} | {stored:N0} | {fileBytes / 1024.0 / 1024.0:N1} MB | {bytesPerVersion:N0} B | {snapshot / length:N0} B | {aspectDelta / length:N0} B | {facetDelta / length:N0} B | {withFacetDeltas:N0} B | {(double)bytesPerVersion / withFacetDeltas:N1}× |");
        }
    }

    private static long Size(object value) => JsonSerializer.SerializeToUtf8Bytes(value).LongLength;

    private static long Checkpoint(string databasePath)
    {
        BenchmarkData.CheckpointWal(databasePath);
        return new FileInfo(databasePath).Length + (File.Exists(databasePath + "-wal") ? new FileInfo(databasePath + "-wal").Length : 0);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (databasePath is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
