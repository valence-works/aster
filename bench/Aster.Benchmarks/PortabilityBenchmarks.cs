using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Benchmarks;

/// <summary>
/// Export of 100 selected resources (all versions) and import planning of that snapshot back into the same
/// store, where every item is reused (the preview reads the target state and diffs, but writes nothing).
/// </summary>
[BenchmarkCategory("Portability")]
public class PortabilityBenchmarks : AsterBenchmarkBase
{
    private PortableSnapshot snapshot = null!;

    private IResourcePortabilityService Portability => Data.Services.GetRequiredService<IResourcePortabilityService>();

    [GlobalSetup(Target = nameof(PreviewImportAsync))]
    public async Task SetupSnapshotAsync()
    {
        await GlobalSetupAsync();
        snapshot = (await Portability.ExportAsync(Request())).Snapshot!;
    }

    [Benchmark(Description = "Export (100 resources)")]
    public Task<PortableSnapshotExportResult> ExportAsync() => Portability.ExportAsync(Request()).AsTask();

    [Benchmark(Description = "Import preview (100 resources)")]
    public Task<PortableImportPreview> PreviewImportAsync() => Portability.PreviewImportAsync(snapshot).AsTask();

    private PortableSnapshotExportRequest Request() => new()
    {
        ScopeMode = PortableExportScopeMode.SelectedResources,
        ResourceIds = Enumerable.Range(0, Math.Min(100, Data.ArticleCount)).Select(BenchmarkData.ArticleId).ToHashSet(StringComparer.Ordinal),
    };
}
