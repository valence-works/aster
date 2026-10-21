using Aster.Core.Abstractions;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// Side-effect-free provider identity for the SQL Server JSON query provider.
/// </summary>
internal sealed class SqlServerJsonQueryProviderIdentity : IResourceQueryProviderIdentity
{
    /// <inheritdoc />
    public string ProviderKey => SqlServerJsonQueryCapabilitiesProvider.ProviderKey;
}
