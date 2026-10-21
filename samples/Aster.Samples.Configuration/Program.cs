// Feature flags and app configuration promoted through dev, staging and prod with Aster
// activation channels (#119). Run with --sqlite to store the configuration in a temporary
// SQLite file instead of memory.

using Microsoft.Data.Sqlite;
using Aster.Samples.Configuration;

var useSqlite = args.Contains("--sqlite", StringComparer.OrdinalIgnoreCase);
var databasePath = useSqlite ? Path.Combine(Path.GetTempPath(), $"aster-configuration-sample-{Guid.NewGuid():N}.db") : null;

Console.WriteLine($"Aster configuration sample ({(useSqlite ? $"SQLite: {databasePath}" : "in-memory store")})");
Console.WriteLine();

try
{
    await using var store = ConfigurationStore.Build(databasePath);
    await ConfigurationScenario.RunAsync(store, Console.Out);
}
finally
{
    if (databasePath is not null)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            File.Delete(file);
    }
}
