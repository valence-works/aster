# Aster.Elsa.Agents

[Elsa Workflows](https://github.com/elsa-workflows/elsa-core) 3 agent definitions on [Aster](https://github.com/valence-works/aster). It's an `IAgentStore` for Elsa's agent persistence feature:

- **Versions:** every save is a new Aster resource version.
- **Channels:** Elsa reads, for each agent, the version active in a configured channel.
- **From Aster:** history, promotion between channels, rollback and export.
- **Migration:** a one-time import brings agents over from Elsa's own store.

Elsa itself is unchanged: the store plugs into `AgentPersistenceFeature.UseAgentStore`.

**Supported Elsa versions:** 3.9.0 and later 3.x releases (`[3.9.0, 4.0.0)`). Targets .NET 8, 9 and 10.

## Register

The host registers Aster with the provider that matches its Elsa database, then selects the Aster store in Elsa's agent persistence feature:

```csharp
using Aster.Core.Extensions;
using Aster.Elsa.Agents;
using Elsa.Agents;
using Elsa.Extensions;

builder.Services.AddAsterCore();
builder.Services.AddAsterSqlServerJson(options => options.ConnectionString = connectionString); // or AddAsterPostgresJsonb, AddAsterSqliteJson

builder.Services.AddElsa(elsa => elsa
    .UseAgents()
    .UseAgentPersistence(agents => agents.UseAsterAgentStore(options =>
    {
        options.Channel = "production";   // the default
        options.AutoActivate = true;      // the default: a save is live at once, as with Elsa's own stores
    })));
```

- **The call is the feature flag.** `UseAsterAgentStore` passes the store to Elsa's `UseAgentStore`. Leave it out and Elsa keeps its own store.
- **Don't combine it with Elsa's EF Core store.** `UseEntityFrameworkCore` on the same feature selects Elsa's EF Core store again while Elsa applies its features, so the EF Core store would win.
- **Classic feature system only.** Elsa 3.9's agent persistence has no CShells shell feature (its `AgentsPersistenceShellFeature.cs` is empty), so there is no shell-feature seam to plug into.
- **The definition registers itself.** The store registers the `ElsaAgent` resource definition in each tenant on its first write there. `AsterAgentResource.CreateDefinition()` returns it for hosts that register definitions at deploy time.

## How agents are stored

An agent is one `ElsaAgent` resource. Its `ResourceId` is the Elsa agent ID. It has two aspects:

| Aspect key | Holds | Facets |
|---|---|---|
| `Agent` | `AgentDefinition.Name` and `Description` | `Name`, `Description` (`string`) |
| `AgentConfig` | `AgentDefinition.AgentConfig`, field for field | `Name`, `FunctionName`, `Description`, `PromptTemplate` (`string`); `Skills` (`string[]`); `Agents` (`reference[]` to `ElsaAgent`); `OutputVariable`, `ExecutionSettings` (`object`). `InputVariables` is stored with the aspect but is not a facet: a collection of objects is not a facet type. |

`AsterAgentResource` has the definition ID and the aspect keys as constants, for queries such as "agents that use the image skill":

```csharp
var agents = await queries.QueryAsync(new ResourceQuery
{
    DefinitionId = AsterAgentResource.DefinitionId,
    Filter = new FacetValueFilter(AsterAgentResource.ConfigAspectKey, "Skills", "ImageGenerator", ComparisonOperator.ArrayContains),
});
```

### Reads, writes and deletes

| `IAgentStore` call | On Aster |
|---|---|
| `AddAsync` | Creates version 1, with the Elsa ID as `ResourceId` (Aster generates one when it is empty), and activates it in the channel when `AutoActivate` is set. An existing ID, including a deleted agent's, throws `DuplicateResourceIdException`, as Elsa's in-memory store throws on a duplicate key. |
| `UpdateAsync` | Saves a new version on top of the latest one and activates it when `AutoActivate` is set. The last writer wins, as in Elsa's stores; a concurrent save is retried up to three times. An unknown ID is added. A deleted or archived agent throws `InvalidOperationException` rather than saving a version nobody can read; restore it first. |
| `GetAsync`, `FindAsync`, `ListAsync` | Return, for each agent, the version active in the channel (the highest when several are, as in an A/B test). Agents with no active version, or that are archived or soft-deleted, are left out. Filters compare ordinally, as Elsa's in-memory store does; results are ordered by ID. |
| `DeleteAsync`, `DeleteManyAsync` | Soft-delete: a `SoftDeleted` lifecycle marker. Versions, activations and history stay, and `IResourceLifecycleRestoreService` brings the agent back. Deleting an unknown or already deleted agent does nothing. |

An agent must have an `AgentConfig`: Elsa builds every agent and its activity from it, so the store refuses one without it (`ArgumentException`).

### Channels, promotion and rollback

The store reads one channel. `AutoActivate` decides whether saves are live at once:

- **`AutoActivate = true` (default).** Every save is activated in the channel, so Elsa behaves as with its own stores. History and rollback come from Aster.
- **`AutoActivate = false`.** Saves are drafts. Elsa keeps running the active version until the host promotes a new one.

Promotion and rollback are Aster activations:

```csharp
// Promote version 4 of an agent, or roll back to version 2: Elsa reads it from the next call on.
await resources.ActivateAsync(agentId, 4, "production");
await resources.ActivateAsync(agentId, 2, "production");
```

Two hosts can read different channels of the same database, for example a staging host with `Channel = "staging"` and production with `"production"`. Elsa caches agent activities in its activity registry, which it refreshes when its agent manager saves or deletes. After an activation through Aster, refresh it with `IActivityRegistry.RefreshDescriptorsAsync`.

The `Aster.Elsa` activities (Activate Version, Promote Resource, Get Resource) and `Aster.AspNetCore`'s endpoints work on agent resources like any other resource.

### Agent references

Elsa names the agents an agent works with (`AgentConfig.Agents`) by agent name. Aster references point at resource IDs, so the store translates:

- **On save:** each name becomes the resource ID of the agent with that name. It looks first among the agents visible in the channel, then among all agents that are not deleted (lowest ID first), then among the agents this one already references. That last step keeps a reference to an agent deleted since saving as it reads.
- **On read:** each ID becomes that agent's current name. That is the name in the channel when the agent is active there, otherwise its latest name.
- **A reference follows the agent, not the name:** after a rename, the referencing agent reads the new name.
- **Reverse lookup works:** `IResourceReferenceService.GetReferrersAsync(agentId)` lists the agents that use an agent, before you change or delete it.
- **Unknown names:** a name that matches no agent is stored as written. Aster's default reference validation (`ReferenceValidationMode.Strict`) then rejects the save with `FacetValueValidationException` (`reference-target-missing`). Under `Lenient` the name is kept and read back unchanged.

Skills are not references. Elsa registers skills in code (`ISkillsProvider`), not in a store, so there is nothing to point at, and `Skills` is a `string[]` facet.

## Tenants

Every call runs in the Aster tenant mapped from Elsa's current tenant, using the same mapping as `Aster.Elsa` (`AsterTenantMapping`):

- **Elsa's default tenant** (`""`) is Aster's default scope.
- **Any other ID** is the same Aster tenant ID.
- **Refused:** `*` and `default` throw `TenantScopeException`.

Agents, names and references are per tenant.

## Migrating from Elsa's store

`IAsterAgentImporter` copies agents from any `IAgentStore` into the Aster store, keeping their IDs:

```csharp
// A one-off migration host: Elsa still on its EF Core store, Aster registered beside it.
builder.Services.AddAsterCore();
builder.Services.AddAsterSqlServerJson(options => options.ConnectionString = connectionString);
builder.Services.AddAsterElsaAgents();
builder.Services.AddElsa(elsa => elsa
    .UseAgents()
    .UseAgentPersistence(agents => agents.UseEntityFrameworkCore(ef => ef.UseSqlServer(connectionString))));

// For each Elsa tenant (push it with ITenantAccessor.PushContext first):
using var scope = app.Services.CreateScope();
var result = await scope.ServiceProvider.GetRequiredService<IAsterAgentImporter>()
    .ImportAsync(scope.ServiceProvider.GetRequiredService<IAgentStore>());   // Elsa's EF Core store here
```

Then switch the host to `UseAsterAgentStore()`.

- **Idempotent.** An agent whose ID already exists in Aster, in any lifecycle state, is skipped and left as it is. Running the import again imports only what is new.
- **Imported agents are live.** Each one is activated in the store's channel, whatever `AutoActivate` says.
- **References resolve whatever the order.** Agents are imported after the agents they name. Agents in a reference cycle (or that name themselves), and agents that depend on one, are saved twice:
  - version 1 without the references into the cycle;
  - version 2 with all of them.
- **Failures don't stop it.** A failing agent, such as one that names an agent that doesn't exist, is reported in `AsterAgentImportResult.Items` with its error, and the import goes on.
  - If version 2 of a cycle agent fails, version 1 (without those references) stays in Aster, so a re-run skips it. Fix it with an update through Elsa.
- **Tenants.** The import reads and writes in Elsa's current tenant. Run it once per tenant.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Channel` | `production` | The channel the store reads, and activates saves in. It can't be blank or start or end with whitespace. |
| `AutoActivate` | `true` | Activate every save in `Channel`. With `false`, saves are drafts until activated through Aster. |
