using BenchmarkDotNet.Attributes;

namespace Aster.Benchmarks;

/// <summary>
/// Shared parameters and seeded data. Data sizes come from <c>ASTER_BENCH_VERSIONS</c> (comma-separated total
/// resource versions; default <c>10000</c>), so larger runs need no code change.
/// </summary>
public abstract class AsterBenchmarkBase
{
    private int cursor;

    [ParamsAllValues]
    public ProviderKind Provider { get; set; }

    [ParamsSource(nameof(Sizes))]
    public int Versions { get; set; }

    public static IEnumerable<int> Sizes() =>
        (Environment.GetEnvironmentVariable("ASTER_BENCH_VERSIONS") ?? "10000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse);

    internal BenchmarkData Data { get; private set; } = null!;

    [GlobalSetup]
    public async Task GlobalSetupAsync() => Data = await BenchmarkData.CreateAsync(Provider, Versions);

    [GlobalCleanup]
    public async Task GlobalCleanupAsync() => await Data.DisposeAsync();

    /// <summary>Rotates over the seeded articles so successive invocations hit different rows.</summary>
    protected int NextArticle() => Interlocked.Increment(ref cursor) % Data.ArticleCount;
}
