using Aster.Benchmarks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

// A short job keeps a full run in minutes at 10k versions unless --job is given (e.g. --job default for
// publication-grade statistics, --job dry to smoke-test every scenario).
if (!args.Any(static arg => arg.StartsWith("--job", StringComparison.OrdinalIgnoreCase) || arg == "-j"))
    args = [.. args, "--job", "short"];

var config = DefaultConfig.Instance
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(MarkdownExporter.GitHub, JsonExporter.Brief)
    .AddColumn(CategoriesColumn.Default)
    .WithOptions(ConfigOptions.JoinSummary)
    .HideColumns(Column.Error, Column.StdDev);

BenchmarkSwitcher.FromAssembly(typeof(AsterBenchmarkBase).Assembly).Run(args, config);
