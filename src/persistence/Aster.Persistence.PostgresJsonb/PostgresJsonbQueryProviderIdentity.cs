using Aster.Core.Abstractions;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// Side-effect-free provider identity for the PostgreSQL JSONB query provider.
/// </summary>
internal sealed class PostgresJsonbQueryProviderIdentity : IResourceQueryProviderIdentity
{
    /// <inheritdoc />
    public string ProviderKey => PostgresJsonbQueryCapabilitiesProvider.ProviderKey;
}
