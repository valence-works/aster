using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using BenchmarkDotNet.Attributes;

namespace Aster.Benchmarks;

/// <summary>
/// Single-resource writes and reads through <c>IResourceManager</c>, with validation and lifecycle hooks as
/// hosts use them. Writes add rows, so later invocations run against slightly more data than seeded.
/// </summary>
[BenchmarkCategory("Resources")]
public class ResourceBenchmarks : AsterBenchmarkBase
{
    private int created;

    [Benchmark(Description = "Create")]
    public Task<Resource> CreateAsync()
    {
        var index = Interlocked.Increment(ref created);
        return Data.Manager.CreateAsync("Article", new CreateResourceRequest
        {
            ResourceId = $"bench-new-{Guid.NewGuid():N}",
            InitialAspects = BenchmarkData.ArticleAspects(index, 1),
        }).AsTask();
    }

    [Benchmark(Description = "Update (append a version)")]
    public async Task<Resource> UpdateAsync()
    {
        var resourceId = BenchmarkData.ArticleId(NextArticle());
        var latest = await Data.Manager.GetLatestVersionAsync(resourceId);
        return await Data.Manager.UpdateAsync(resourceId, new UpdateResourceRequest
        {
            BaseVersion = latest!.Version,
            AspectUpdates = BenchmarkData.ArticleAspects(1, latest.Version + 1),
        });
    }

    [Benchmark(Description = "Activate")]
    public Task ActivateAsync() =>
        Data.Manager.ActivateAsync(BenchmarkData.ArticleId(NextArticle()), 1, "preview").AsTask();

    [Benchmark(Description = "Get latest")]
    public Task<Resource?> GetLatestAsync() =>
        Data.Manager.GetLatestVersionAsync(BenchmarkData.ArticleId(NextArticle())).AsTask();

    [Benchmark(Description = "Get active")]
    public async Task<List<Resource>> GetActiveAsync() =>
        (await Data.Manager.GetActiveVersionsAsync(BenchmarkData.ArticleId(NextArticle() & ~1), BenchmarkData.Published)).ToList();
}
