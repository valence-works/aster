using Microsoft.Data.Sqlite;

namespace Aster.Persistence.SqliteJson.Querying;

internal sealed class SqliteParameterBag
{
    private readonly List<(string Name, object? Value)> parameters = [];

    public string Add(object? value)
    {
        var name = $"$p{parameters.Count}";
        parameters.Add((name, value));
        return name;
    }

    public void ApplyTo(SqliteCommand command)
    {
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
