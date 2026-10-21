using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Tenancy;

namespace Aster.Elsa.Agents;

/// <summary>
/// Registers the <c>ElsaAgent</c> definition in a tenant before the first write there. Registration is conditional
/// (<see cref="IResourceDefinitionStore.RegisterDefinitionIfLatestAsync"/> with version <c>0</c>), so concurrent first
/// writes, or several processes, register it once. Tenants already checked are remembered for the process lifetime.
/// </summary>
internal sealed class AgentDefinitionRegistrar(IResourceDefinitionStore definitions)
{
    private readonly ConcurrentDictionary<string, bool> registered = new(StringComparer.Ordinal);

    public async ValueTask EnsureRegisteredAsync(TenantScope tenant, CancellationToken cancellationToken)
    {
        if (registered.ContainsKey(tenant.TenantId))
            return;

        if (await definitions.GetDefinitionAsync(AsterAgentResource.DefinitionId, tenant, cancellationToken) is null)
        {
            // A version conflict means another writer registered it first, which is just as good.
            await definitions.RegisterDefinitionIfLatestAsync(
                AsterAgentResource.CreateDefinition() with { TenantScope = tenant },
                expectedLatestVersion: 0,
                tenant,
                cancellationToken);
        }

        registered.TryAdd(tenant.TenantId, true);
    }
}
