namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// Options for the PostgreSQL JSONB provider.
/// </summary>
public sealed class PostgresJsonbAsterOptions
{
    /// <summary>
    /// Gets or sets the Npgsql connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the provider creates its tables on first use. Bootstrap is idempotent and safe
    /// when several processes start at once. Turn it off when the schema is created out of band.
    /// </summary>
    public bool InitializeSchema { get; set; } = true;
}
