using Aster.Core.Abstractions;

namespace Aster.Persistence.SqliteJson;

/// <summary>
/// Side-effect-free provider identity for the SQLite JSON query provider.
/// </summary>
internal sealed class SqliteJsonQueryProviderIdentity : IResourceQueryProviderIdentity
{
    /// <inheritdoc />
    public string ProviderKey => SqliteJsonQueryCapabilitiesProvider.ProviderKey;
}
