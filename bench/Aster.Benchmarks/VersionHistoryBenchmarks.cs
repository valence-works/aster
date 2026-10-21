using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Benchmarks;

/// <summary>
/// Reads and writes on resources with long histories (#105): a few resources with
/// <see cref="VersionsPerResource"/> full-snapshot versions each, edited the way <see cref="HistoryData"/>
/// describes. Answers whether reads slow down as one resource accumulates versions.
/// </summary>
[BenchmarkCategory("History")]
public class VersionHistoryBenchmarks
{
    private int cursor;
    private HistoryData data = null!;

    [ParamsAllValues]
    public ProviderKind Provider { get; set; }

    [ParamsSource(nameof(HistoryLengths))]
    public int VersionsPerResource { get; set; }

    public static IEnumerable<int> HistoryLengths() => HistoryData.HistoryLengths();

    [GlobalSetup]
    public async Task GlobalSetupAsync() => data = await HistoryData.CreateAsync(Provider, VersionsPerResource);

    [GlobalCleanup]
    public async Task GlobalCleanupAsync() => await data.DisposeAsync();

    private string Next() => HistoryData.ResourceId(Interlocked.Increment(ref cursor) % HistoryData.Resources);

    [Benchmark(Description = "Get latest")]
    public Task<Resource?> GetLatestAsync() => data.Manager.GetLatestVersionAsync(Next()).AsTask();

    [Benchmark(Description = "Get oldest version")]
    public Task<Resource?> GetOldestAsync() => data.Manager.GetVersionAsync(Next(), 1).AsTask();

    [Benchmark(Description = "Get all versions")]
    public async Task<int> GetAllVersionsAsync() => (await data.Manager.GetVersionsAsync(Next())).Count();

    [Benchmark(Description = "Version history")]
    public async Task<int> HistoryAsync() =>
        (await data.Services.GetRequiredService<IResourceVersionHistoryService>()
            .GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = Next() })).Versions.Count;

}

/// <summary>
/// Appending a version to a resource with a long history. Every call grows the history it measures, so a
/// fixed <see cref="InvocationCountAttribute">invocation count</see> skips the pilot stage and bounds the
/// drift: a <c>ShortRun</c> calls it about 400 times, about 20 extra versions per resource.
/// </summary>
[BenchmarkCategory("History")]
[InvocationCount(64)]
public class VersionHistoryUpdateBenchmarks
{
    private int cursor;
    private HistoryData data = null!;

    [ParamsAllValues]
    public ProviderKind Provider { get; set; }

    [ParamsSource(nameof(HistoryLengths))]
    public int VersionsPerResource { get; set; }

    public static IEnumerable<int> HistoryLengths() => HistoryData.HistoryLengths();

    [GlobalSetup]
    public async Task GlobalSetupAsync() => data = await HistoryData.CreateAsync(Provider, VersionsPerResource);

    [GlobalCleanup]
    public async Task GlobalCleanupAsync() => await data.DisposeAsync();

    [Benchmark(Description = "Update (append a version)")]
    public async Task<Resource> UpdateAsync()
    {
        var resourceId = HistoryData.ResourceId(Interlocked.Increment(ref cursor) % HistoryData.Resources);
        var latest = await data.Manager.GetLatestVersionAsync(resourceId);
        return await data.Manager.UpdateAsync(resourceId, new UpdateResourceRequest
        {
            BaseVersion = latest!.Version,
            AspectUpdates = HistoryData.Aspects(latest.Version + 1),
        });
    }
}
