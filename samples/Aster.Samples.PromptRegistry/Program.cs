// A versioned registry of AI prompts, tools and agents (#120): draft, candidate and production
// channels, an A/B test with two versions active in production, and a prompt set exported from
// one environment and imported into another with remapping. Run with --sqlite to store both
// environments in temporary SQLite files instead of memory.

using Aster.Samples.PromptRegistry;

var useSqlite = args.Contains("--sqlite", StringComparer.OrdinalIgnoreCase);
Console.WriteLine($"Aster prompt registry sample ({(useSqlite ? "SQLite" : "in-memory")} stores)");
Console.WriteLine();
await PromptRegistryScenario.RunAsync(useSqlite, Console.Out);
