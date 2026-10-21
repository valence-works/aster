using System.Data;
using Microsoft.Data.SqlClient;

namespace Aster.Persistence.SqlServerJson.Querying;

internal sealed class SqlServerParameterBag
{
    private readonly List<SqlParameter> parameters = [];

    public string Add(object value)
    {
        var name = $"@p{parameters.Count}";

        // Strings bind as nvarchar(4000) (or max): one declared size per query shape keeps its plan reusable.
        parameters.Add(value is string text
            ? new SqlParameter(name, SqlDbType.NVarChar, text.Length > 4000 ? -1 : 4000) { Value = text }
            : new SqlParameter(name, value));
        return name;
    }

    public void ApplyTo(SqlCommand command)
    {
        foreach (var parameter in parameters)
            command.Parameters.Add(parameter);
    }
}
