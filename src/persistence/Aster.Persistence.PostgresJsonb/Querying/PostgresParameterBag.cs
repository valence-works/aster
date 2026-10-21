using Npgsql;

namespace Aster.Persistence.PostgresJsonb.Querying;

internal sealed class PostgresParameterBag
{
    private readonly List<NpgsqlParameter> parameters = [];

    public string Add(object value)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, value));
        return name;
    }

    public void ApplyTo(NpgsqlCommand command)
    {
        foreach (var parameter in parameters)
            command.Parameters.Add(parameter);
    }
}
