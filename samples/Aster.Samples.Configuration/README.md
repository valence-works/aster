# Feature flags and app configuration with per-environment channels

Reference sample for [#119](https://github.com/valence-works/aster/issues/119) (epic [#70](https://github.com/valence-works/aster/issues/70)).

Configuration is authored once, promoted through `dev`, `staging` and `prod`, and rolled back instantly when something goes wrong. Running applications read the configuration active in their own environment through the standard `IConfiguration` and `IOptionsMonitor<T>` APIs, and they reload the moment a version is promoted or rolled back.

## Run it

```bash
dotnet run --project samples/Aster.Samples.Configuration
dotnet run --project samples/Aster.Samples.Configuration -- --sqlite   # same walkthrough on a temporary SQLite file
```

The in-memory store needs no setup. The program starts two application instances, one in `staging` and one in `prod`, then:

1. saves version 1 of a `new-checkout` flag (on for 10% of tenant `acme`) and of the `Checkout` settings section (`legacy-gateway`), and releases both to `dev`;
2. promotes version 1 to `staging`, then to `prod`; each running instance reloads as its channel changes;
3. saves version 2 (50% rollout, adds tenant `globex`; `stripe`) and promotes it to `staging` only, so the two instances now differ;
4. promotes version 2 to `prod`;
5. rolls the `Checkout` section in `prod` back to version 1 while the flag stays on version 2;
6. prints the version history: nothing was rewritten, only the `prod` activation moved.

## Aster capabilities it exercises

| Capability | Where |
|---|---|
| Typed aspects with derived facets, including `string[]` and `object` | [`Model/FeatureFlag.cs`](Model/FeatureFlag.cs), [`Model/AppSettings.cs`](Model/AppSettings.cs) |
| Facet metadata enforced on save (`Pattern`, `Minimum`/`Maximum`, `UniqueItems`) | [`Model/ConfigurationDefinitions.cs`](Model/ConfigurationDefinitions.cs) |
| Immutable versions with optimistic concurrency | `ConfigurationReleases.SaveAsync` |
| Activation channels as environments | [`Releases/ConfigurationReleases.cs`](Releases/ConfigurationReleases.cs) |
| Historical activation (021) for rollback | `ConfigurationReleases.RollbackAsync` |
| Queries over the active version in a channel | [`ChannelConfiguration/ChannelConfigurationLoader.cs`](ChannelConfiguration/ChannelConfigurationLoader.cs) |
| Lifecycle hooks after activation and deactivation | [`ChannelConfiguration/ConfigurationReloadHook.cs`](ChannelConfiguration/ConfigurationReloadHook.cs) |
| Version history inspection (020) | `ConfigurationScenario`, last step |
| In-memory and SQLite JSON providers | [`ConfigurationStore.cs`](ConfigurationStore.cs) |

## How it fits together

**Definitions.** `FeatureFlag` has one aspect with `Key`, `Description`, `Enabled`, `RolloutPercentage` (0–100) and `AllowedTenants` (`string[]`, unique). `AppSettings` has a `Section` name and `Values`, an `object` facet that holds any JSON object. Both are declared with `WithAspect<T>()`, so the facets come from the record properties. Each flag or section is one resource (`feature-flag.new-checkout`, `app-settings.checkout`), and each save appends a version.

**Environments are channels.** Releasing activates a version in a channel with single-active semantics, so a channel always holds at most one version of each item. Promotion reads the version active in one channel and activates it in the next. Rollback activates an older version in one channel: the other channels, the latest version and the history are untouched.

**Configuration provider.** `AsterChannelConfigurationProvider` is an ordinary `IConfigurationProvider`. It queries the resources active in its channel and flattens them into keys:

```text
FeatureFlags:new-checkout:Enabled            = true
FeatureFlags:new-checkout:RolloutPercentage  = 50
FeatureFlags:new-checkout:AllowedTenants:0   = acme
Checkout:PaymentProvider                     = stripe
Checkout:Retry:MaxAttempts                   = 5
Checkout:Currencies:0                        = EUR
```

The application binds `FeatureFlagsOptions` and `CheckoutOptions` from these sections with `services.Configure<T>(section)` and reads them through `IOptionsMonitor<T>`. Application code never references Aster.

**Reload.** `ConfigurationReloadHook` is a lifecycle hook registered in the Aster container. After every activation or deactivation of a `FeatureFlag` or `AppSettings` resource, it notifies the providers; a provider whose channel matches reloads and raises its change token, which refreshes `IOptionsMonitor<T>`. The hook runs before `ActivateAsync` returns, so a promotion or rollback is visible to the application as soon as the call completes. A provider that fails to reload keeps its last good configuration and records the error in `LastReloadError`, rather than failing the activation that already happened.

**Two containers.** Configuration is built before an application's own service provider, so the Aster store lives in its own container ([`ConfigurationStore`](ConfigurationStore.cs)) and the configuration source takes that provider. `AddChannelConfigurationStore()` adds the loader, notifier and hook to it. In a real deployment the store would typically sit behind a service that applications reach over the network; the provider and hook are the parts to reuse.

## Design notes

- **Promotion is two calls.** Aster deliberately has no core "promote from channel A to channel B" API (Elsa spike finding F7, see [the decisions](../../docs/consumers/elsa-spike.md#decisions)), so `ConfigurationReleases.PromoteAsync` reads the active version in one channel and activates it in the next.
- **Tenants.** `AllowedTenants` names tenants of the application, not Aster tenant scopes. The sample keeps all configuration in Aster's default tenant; a multi-tenant store would pass a `TenantScope` to the queries and filter hook notifications by `context.TenantScope`.
- **Initial load is synchronous** because `IConfigurationBuilder.Build()` is. Reloads after activations are asynchronous.
- **Rollout bucketing** hashes `"{flag}:{subject}"` with SHA-256, so a subject stays in or out of a rollout across reloads and processes.

## Tests

[`test/Aster.Samples.Configuration.Tests`](../../test/Aster.Samples.Configuration.Tests) covers promotion, rollback and reload on both providers, including that activations in other channels do not reload an application, that deactivation removes configuration, and that the walkthrough above completes.

```bash
dotnet test test/Aster.Samples.Configuration.Tests
```
