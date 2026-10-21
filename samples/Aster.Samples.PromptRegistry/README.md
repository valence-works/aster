# AI prompt and agent registry with A/B channels

Reference sample for [#120](https://github.com/valence-works/aster/issues/120) (epic [#70](https://github.com/valence-works/aster/issues/70)). It prepares the Elsa agents use case.

Prompts, tools and agents are versioned resources. A new version starts as a draft, is evaluated in a `candidate` channel, and is promoted to `production`. A/B tests run with two versions active in `production` at once. A prompt set can be exported from one environment and imported into another, including one where some IDs are already taken.

## Run it

```bash
dotnet run --project samples/Aster.Samples.PromptRegistry
dotnet run --project samples/Aster.Samples.PromptRegistry -- --sqlite   # same walkthrough on temporary SQLite files
```

The in-memory store needs no setup. The program:

1. saves two tools, prompt v1 and an agent in the `eu` environment; the history shows every version as a draft;
2. releases everything to `candidate`, resolves the agent there, then promotes everything to `production`;
3. saves prompt v2 and releases it to `candidate`, so `candidate` resolves v2 while `production` still serves v1;
4. starts an A/B test that activates v2 in `production` next to v1. Each conversation resolves to one arm and stays on it, and 1,000 conversations split about evenly;
5. concludes the test with v2 as the winner, which deactivates v1 in `production`, and renders the winning template;
6. exports the agent with everything it references from `eu` and imports it into `us`, where the team already has its own `prompt.support-triage`. The incoming prompt is remapped to `prompt.support-triage__imported`, the agent's reference is rewritten to match, and the `us` prompt is left alone.

## Aster capabilities it exercises

| Capability | Where |
|---|---|
| Typed aspects with derived `string[]` and `object` facets | [`Model/Prompt.cs`](Model/Prompt.cs), where `ModelSettings` is a nested record stored as `object` |
| `reference` and `reference[]` facets derived with `[ReferenceFacet]` and `TargetDefinitionIds`, checked on save | [`Model/Agent.cs`](Model/Agent.cs), `AgentDefinition` in [`Model/RegistryDefinitions.cs`](Model/RegistryDefinitions.cs) |
| Drafts (versions active in no channel), single-active channels | [`Registry/PromptRegistryService.cs`](Registry/PromptRegistryService.cs) |
| `allowMultipleActive` for A/B tests, `DeactivateAsync` to end them | `StartAbTestAsync`, `ConcludeAbTestAsync` |
| Batch version history (023) | `PromptRegistryScenario.DescribeAsync` |
| Export with `IncludeReferencedResources`, JSON snapshots, import preview, `RemapDivergent` with reference rewriting (#89) | [`Portability/PromptSetTransfer.cs`](Portability/PromptSetTransfer.cs) |
| In-memory and SQLite JSON providers | [`RegistryEnvironment.cs`](RegistryEnvironment.cs) |

## How it fits together

**Definitions.** `Prompt` (name, template, `Variables` as `string[]`, `Model` as `object`) and `Tool` (name, description) derive their facets from records. So does `Agent`, whose `Prompt` and `Tools` properties are marked `[ReferenceFacet]`: `Prompt` becomes a `reference` to a `Prompt` and `Tools` a `reference[]` to `Tool`s. Aster never infers references; the attribute is the explicit opt-in. With the default strict reference validation, saving an agent that points at a missing prompt, or at a prompt where a tool is expected, fails.

**Channels.** A saved version is active nowhere, which makes it a draft. `ReleaseToCandidateAsync` makes it the one version in `candidate`. `PromoteToProductionAsync` activates the candidate version in `production` as its only version. Promotion is a host helper rather than an SDK call, as the Elsa spike decided (F7 in [the spike findings](../../docs/consumers/elsa-spike.md#decisions)).

**A/B tests.** `StartAbTestAsync` activates the candidate in `production` with `allowMultipleActive: true`, so `production` holds two versions. `ResolveAsync(agentId, channel, subjectId)` resolves the agent, its prompt and its tools at the versions active in the channel. Where more than one version is active, it hashes the resource ID with the subject to pick one, so a conversation always gets the same arm. Any of the three can be under test this way. `ConcludeAbTestAsync` deactivates the losing arms, and `PromoteToProductionAsync` replaces both arms.

**Portability.** `PromptSetTransfer.ExportAsync` exports the selected agents with `IncludeReferencedResources`, which brings their prompts and tools along. The export includes every version, the activation state and the definitions, serialized to JSON as it would travel between environments. The import uses `RemapDivergent`: an item whose ID exists in the target with different content gets a new ID, and every reference to it in the imported payloads is rewritten. The preview reports each remap and rewrite before anything is written.

## Design notes and SDK gaps found

- **Definitions get stable IDs (workaround).** `ResourceDefinitionBuilder` assigns random IDs to the definition, its aspects and its facets, and import compares definitions including those IDs. When two environments register the same definitions from the same code, an import therefore sees them as divergent. Under `RemapDivergent` it moves the imported resources to new `Prompt__imported`, `Tool__imported` and `Agent__imported` definitions; under `Strict` it fails. `RegistryDefinitions.WithStableIds` derives the IDs from names, so the definitions match and are reused. This is the definition-level counterpart of the problem #195 fixed for shared aspects.
- **Arm assignment is even and stateless.** The selector splits traffic evenly over the active versions. Weighted splits would need per-version weights stored somewhere, such as a lifecycle marker or a separate experiment resource; that is outside this sample.
- **Channel-consistent resolution.** An agent resolves its prompt and tools in its own channel, so a `candidate` agent runs with `candidate` prompts. The definitions keep the default `latest` reference resolution, because a fixed `ReferenceResolution` channel would force every referenced prompt to be active in that one channel before an agent could be saved.

## Tests

[`test/Aster.Samples.PromptRegistry.Tests`](../../test/Aster.Samples.PromptRegistry.Tests) covers promotion, A/B selection and portability on both providers, plus reference validation, template rendering and the walkthrough above.

```bash
dotnet test test/Aster.Samples.PromptRegistry.Tests
```
