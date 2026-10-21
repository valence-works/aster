using Aster.Benchmarks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

// `storage [lengths]` prints the version storage report for #105 (docs/adr/0005-version-storage.md) and exits.
if (args is ["storage", ..])
{
    var lengths = args.Length > 1 ? args[1].Split(',').Select(int.Parse) : HistoryData.HistoryLengths();
    await HistoryData.WriteStorageReportAsync(Console.Out, lengths);
    return;
}

// A short job keeps a full run in minutes at 10k versions unless --job is given (e.g. --job default for
// publication-grade statistics, --job dry to smoke-test every scenario).
if (!args.Any(static arg => arg.StartsWith("--job", StringComparison.OrdinalIgnoreCase) || arg == "-j"))
    args = [.. args, "--job", "short"];

var config = DefaultConfig.Instance
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Brief) // the GitHub Markdown exporter is already in DefaultConfig
    .AddColumn(CategoriesColumn.Default)
    .WithOptions(ConfigOptions.JoinSummary)
    .HideColumns(Column.Error, Column.StdDev);

BenchmarkSwitcher.FromAssembly(typeof(AsterBenchmarkBase).Assembly).Run(args, config);
