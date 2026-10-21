using Npgsql;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// The provider's own <see cref="NpgsqlDataSource"/> (one connection pool), kept out of the container's
/// <see cref="NpgsqlDataSource"/> registration so it never collides with a host's.
/// </summary>
internal sealed class PostgresJsonbDataSource : IDisposable, IAsyncDisposable
{
    public PostgresJsonbDataSource(PostgresJsonbAsterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        Value = NpgsqlDataSource.Create(options.ConnectionString);
    }

    public NpgsqlDataSource Value { get; }

    public void Dispose() => Value.Dispose();

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}
