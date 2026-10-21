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
    /// Gets or sets whether the provider applies its pending infrastructure steps (tables and indexes) on first use.
    /// Safe when several processes start at once: each step runs once. Turn it off to apply them explicitly, from a
    /// deployment step through <c>IInfrastructureRunner</c>; the provider then never changes the schema itself.
    /// </summary>
    public bool InitializeSchema { get; set; } = true;
}
