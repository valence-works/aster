# Samples

Small, runnable programs that use Aster for a real use case (epic [#70](https://github.com/valence-works/aster/issues/70)). Each one has a README that explains the scenario, which Aster capabilities it exercises, and how to run it. The use-case samples run on the in-memory provider by default, so `dotnet run` needs no setup.

| Sample | Scenario |
|---|---|
| [Aster.Samples.Configuration](Aster.Samples.Configuration) | Feature flags and app configuration promoted through `dev`, `staging` and `prod` channels, with rollback and live `IOptionsMonitor<T>` reload |
| [Aster.Samples.PromptRegistry](Aster.Samples.PromptRegistry) | AI prompts, tools and agents moved through draft, `candidate` and `production`, with A/B tests over two active versions and prompt sets exported and imported between environments |
| [Aster.Elsa.Spike](Aster.Elsa.Spike) | The Elsa consumer spike (#77): a throwaway program that recorded where Aster needed workarounds |
