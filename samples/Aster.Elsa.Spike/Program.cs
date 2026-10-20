// Elsa consumer spike (#77): models three Elsa-like control-plane entities on Aster and
// records every place where Aster forces a workaround. Findings are tagged F1..Fn and
// written up in FRICTION.md. The entity shapes approximate Elsa's (labels on workflow
// definitions, agent configs, secret metadata); they are not copied from Elsa.

using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

var databasePath = Path.Combine(Path.GetTempPath(), $"aster-elsa-spike-{Guid.NewGuid():N}.db");
var activations = new ActivationLog();

var services = new ServiceCollection()
    .AddSingleton(activations)
    .AddAsterCore()
    .AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}")
    .AddResourceLifecycleHook<ActivationNotificationHook>();

await using var provider = services.BuildServiceProvider();
var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
var manager = provider.GetRequiredService<IResourceManager>();
var queries = provider.GetRequiredService<IResourceQueryService>();
var portability = provider.GetRequiredService<IResourcePortabilityService>();
var markers = provider.GetRequiredService<IResourceLifecycleMarkerService>();

var tenantA = TenantScope.FromTenantId("tenant-a");
var tenantB = TenantScope.FromTenantId("tenant-b");
var findings = new Findings();

// ---------------------------------------------------------------------------------------
// Definitions. There is no builder API for declaring facets by name and type (F1), so the
// aspect and facet records are constructed by hand.
// ---------------------------------------------------------------------------------------

var labelDefinition = Model.Definition("Label",
    Model.Aspect("LabelInfo",
        Model.Facet("Name", "string", required: true),
        Model.Facet("Color", "string", metadata: new FacetMetadata { Pattern = "^#[0-9a-f]{6}$" })));

var workflowDefinition = Model.Definition("WorkflowDefinition",
    Model.Aspect("Identity", Model.Facet("Name", "string", required: true), Model.Facet("Description", "string")),
    // Label references: there is no array or reference facet type (F2, F3).
    Model.Aspect("Labels", Model.Facet("LabelIds", "string[]")));

var agentDefinition = Model.Definition("Agent",
    Model.Aspect("Identity", Model.Facet("Name", "string", required: true), Model.Facet("Description", "string")),
    Model.Aspect("Prompt",
        Model.Facet("Template", "string", required: true, metadata: new FacetMetadata { MinLength = 1, UiHint = "multiline" }),
        Model.Facet("InputVariables", "string[]"),
        Model.Facet("OutputVariable", "string")),
    Model.Aspect("Model",
        Model.Facet("Settings", "object")),
    Model.Aspect("Tools",
        Model.Facet("SecretIds", "string[]")));

var secretDefinition = Model.Definition("Secret",
    Model.Aspect("Identity", Model.Facet("Name", "string", required: true)),
    Model.Aspect("SecretInfo",
        Model.Facet("Provider", "string", metadata: new FacetMetadata { AllowedValues = ["AzureKeyVault", "AwsSecretsManager", "Environment"] }),
        Model.Facet("Scope", "string"),
        Model.Facet("ExpiresAt", "datetime")));

foreach (var tenant in new[] { tenantA, tenantB })
{
    foreach (var definition in new[] { labelDefinition, workflowDefinition, agentDefinition, secretDefinition })
        await definitions.RegisterDefinitionAsync(definition with { TenantScope = tenant }, tenant, CancellationToken.None);
}

findings.Observe("F1", "Facets must be declared by constructing AspectDefinition/FacetDefinition records; ResourceDefinitionBuilder has no WithFacet(id, type) and WithAspect<T>() declares no facets.");
findings.Observe("F2", "Unknown facet types such as \"string[]\" and \"object\" register without any diagnostic, so typos also register silently.");

// ---------------------------------------------------------------------------------------
// S1 - Labels as a horizontal aspect on workflow definitions.
// ---------------------------------------------------------------------------------------

var billing = await Create("Label", tenantA, "lbl-billing", ("LabelInfo", new() { ["Name"] = "Billing", ["Color"] = "#1f77b4" }));
var billingEu = await Create("Label", tenantA, "lbl-billing-eu", ("LabelInfo", new() { ["Name"] = "Billing EU", ["Color"] = "#ff7f0e" }));

await Create("WorkflowDefinition", tenantA, "wf-invoice",
    ("Identity", new() { ["Name"] = "Send invoice" }),
    ("Labels", new() { ["LabelIds"] = new[] { billing.ResourceId } }));
await Create("WorkflowDefinition", tenantA, "wf-vat",
    ("Identity", new() { ["Name"] = "EU VAT report" }),
    ("Labels", new() { ["LabelIds"] = new[] { billingEu.ResourceId } }));

// "All workflows labelled Billing". Without array operators the closest portable query is
// Contains on the serialized array.
var labelled = await Query(new ResourceQuery
{
    TenantScope = tenantA,
    DefinitionId = "WorkflowDefinition",
    Filter = new FacetValueFilter("Labels", "LabelIds", billing.ResourceId, ComparisonOperator.Contains),
});
findings.Observe("F3",
    $"Querying workflows labelled '{billing.ResourceId}' with Contains returned [{string.Join(", ", labelled)}]. " +
    (labelled.Contains("wf-vat")
        ? "The substring match also hits 'lbl-billing-eu', a false positive: there is no ArrayContains."
        : "No false positive in this run, but the match is a substring match, not element equality."));

// Invalid colour is rejected (038 metadata works), but a non-existent label ID is accepted.
var invalidColour = await TrySave(() => Create("Label", tenantA, "lbl-bad", ("LabelInfo", new() { ["Name"] = "Bad", ["Color"] = "blue" })));
Check(invalidColour is not null, "Pattern metadata should reject the colour 'blue'.");
var danglingLabel = await TrySave(() => Create("WorkflowDefinition", tenantA, "wf-dangling",
    ("Identity", new() { ["Name"] = "Dangling" }),
    ("Labels", new() { ["LabelIds"] = new[] { "lbl-does-not-exist" } })));
findings.Observe("F4", $"A workflow referencing a non-existent label saved {(danglingLabel is null ? "without error" : "with an error")}: no reference type or integrity check.");

// Required facets are not enforced.
var missingName = await TrySave(() => Create("Label", tenantA, "lbl-unnamed", ("LabelInfo", new() { ["Color"] = "#000000" })));
findings.Observe("F5", $"A Label without its required Name saved {(missingName is null ? "without error: IsRequired is not enforced" : "with an error")}.");

// Type mismatches are not enforced.
var wrongType = await TrySave(() => Create("Secret", tenantA, "sec-wrong-type",
    ("Identity", new() { ["Name"] = "Wrong type" }),
    ("SecretInfo", new() { ["ExpiresAt"] = "not-a-date" })));
findings.Observe("F6", $"A Secret with ExpiresAt = \"not-a-date\" on a datetime facet saved {(wrongType is null ? "without error: declared types are not enforced" : "with an error")}.");

// ---------------------------------------------------------------------------------------
// S2 - Agents promoted through staging and production channels, with rollback.
// ---------------------------------------------------------------------------------------

var openAiKey = await Create("Secret", tenantA, "sec-openai",
    ("Identity", new() { ["Name"] = "OpenAI API key" }),
    ("SecretInfo", new() { ["Provider"] = "AzureKeyVault", ["Scope"] = "agents", ["ExpiresAt"] = DateTimeOffset.UtcNow.AddDays(10) }));
await Create("Secret", tenantA, "sec-smtp",
    ("Identity", new() { ["Name"] = "SMTP password" }),
    ("SecretInfo", new() { ["Provider"] = "Environment", ["Scope"] = "email", ["ExpiresAt"] = DateTimeOffset.UtcNow.AddDays(200) }));

var agentV1 = await Create("Agent", tenantA, "agent-summarizer",
    ("Identity", new() { ["Name"] = "Summarizer" }),
    ("Prompt", new() { ["Template"] = "Summarize: {{input}}", ["InputVariables"] = new[] { "input" }, ["OutputVariable"] = "summary" }),
    ("Model", new() { ["Settings"] = new Dictionary<string, object> { ["model"] = "gpt-x", ["temperature"] = 0.2m } }),
    ("Tools", new() { ["SecretIds"] = new[] { openAiKey.ResourceId } }));

var agentV2 = await manager.UpdateAsync(agentV1.ResourceId, new UpdateResourceRequest
{
    TenantScope = tenantA,
    BaseVersion = agentV1.Version,
    AspectUpdates = new() { ["Prompt"] = new Dictionary<string, object> { ["Template"] = "Summarize in three bullets: {{input}}", ["InputVariables"] = new[] { "input" }, ["OutputVariable"] = "summary" } },
});

await manager.ActivateAsync(agentV1.ResourceId, 1, "production", tenantA, allowMultipleActive: false, CancellationToken.None);
await manager.ActivateAsync(agentV2.ResourceId, 2, "staging", tenantA, allowMultipleActive: false, CancellationToken.None);
Check(await ActiveVersion("agent-summarizer", "staging") == 2, "v2 should be active in staging.");
Check(await ActiveVersion("agent-summarizer", "production") == 1, "v1 should be active in production.");

// Promote staging -> production: the host must look up the version itself (F7).
var stagingVersion = await ActiveVersion("agent-summarizer", "staging");
await manager.ActivateAsync(agentV2.ResourceId, stagingVersion!.Value, "production", tenantA, allowMultipleActive: false, CancellationToken.None);
Check(await ActiveVersion("agent-summarizer", "production") == 2, "v2 should be promoted to production.");

// Rollback production to v1 via historical activation (021).
await manager.ActivateAsync(agentV1.ResourceId, 1, "production", tenantA, allowMultipleActive: false, CancellationToken.None);
Check(await ActiveVersion("agent-summarizer", "production") == 1, "Rollback should make v1 active in production.");
findings.Observe("F7", "Promotion between channels works but needs two calls (read the active version in A, activate it in B); there is no PromoteAsync(resourceId, fromChannel, toChannel). Rollback via historical activation works as designed.");

var activeInProduction = await Query(new ResourceQuery
{
    TenantScope = tenantA,
    DefinitionId = "Agent",
    Scope = ResourceVersionScope.Active,
    ActivationChannel = "production",
});
Check(activeInProduction.SequenceEqual(["agent-summarizer"]), "Exactly one agent should be active in production.");

// Lifecycle hooks as the stand-in for Elsa notifications and triggers.
Check(activations.Events.Count == 4, $"Expected 4 activation notifications, got {activations.Events.Count}.");
findings.Observe("F8", "After-activation hooks fire with ResourceId, Version, Channel and TenantScope but without the DefinitionId, so an Elsa trigger such as 'when an Agent is activated in production' needs an extra read per event.");

// ---------------------------------------------------------------------------------------
// S3 - Secret metadata: expiry queries, tenancy, and lifecycle with inbound references.
// ---------------------------------------------------------------------------------------

var now = DateTimeOffset.UtcNow;
var expiringSoon = await Query(new ResourceQuery
{
    TenantScope = tenantA,
    DefinitionId = "Secret",
    Filter = new FacetValueFilter("SecretInfo", "ExpiresAt", new RangeValue(now, now.AddDays(30)), ComparisonOperator.Range),
});
Check(expiringSoon.Contains("sec-openai") && !expiringSoon.Contains("sec-smtp"), $"Expiry range query returned [{string.Join(", ", expiringSoon)}].");

var tenantBSecrets = await Query(new ResourceQuery { TenantScope = tenantB, DefinitionId = "Secret" });
Check(tenantBSecrets.Count == 0, "Tenant B must not see tenant A secrets.");

// Archive a secret that an agent still uses: no warning is produced (F9).
var archive = await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
{
    TenantScope = tenantA,
    ResourceId = openAiKey.ResourceId,
    State = ResourceLifecycleMarkerState.Archived,
    MarkedAt = now,
    Reason = "rotated",
});
findings.Observe("F9", $"Archiving secret '{openAiKey.ResourceId}' while agent-summarizer references it succeeded={archive.Succeeded} with {archive.Diagnostics.Count} diagnostics: there is no 'used by' lookup.");

// ---------------------------------------------------------------------------------------
// S4 - Export tenant A agents and secrets, import into tenant B.
// ---------------------------------------------------------------------------------------

var export = await portability.ExportAsync(new PortableSnapshotExportRequest
{
    TenantScope = tenantA,
    ScopeMode = PortableExportScopeMode.DefinitionWithResources,
    DefinitionIds = ["Agent", "Secret"],
});
Check(export.Snapshot is not null, $"Export failed: {string.Join("; ", export.Diagnostics.Select(d => d.Code))}");

var import = await portability.ImportAsync(export.Snapshot!, new PortableImportOptions
{
    TargetTenantScope = tenantB,
    CollisionMode = PortableImportCollisionMode.RemapDivergent,
});
var importedAgents = await Query(new ResourceQuery { TenantScope = tenantB, DefinitionId = "Agent" });
var importedSecrets = await Query(new ResourceQuery { TenantScope = tenantB, DefinitionId = "Secret" });
findings.Observe("F10",
    $"Import into tenant B: status={import.Status}, agents=[{string.Join(", ", importedAgents)}], secrets=[{string.Join(", ", importedSecrets)}]. " +
    "IDs are preserved here, so references stay valid; if an ID were remapped, the SecretIds inside agent payloads would not be rewritten, because references are untyped strings.");

// ---------------------------------------------------------------------------------------
// S5 - Developer experience notes that need no runtime evidence.
// ---------------------------------------------------------------------------------------

findings.Observe("F11", "Dictionary-based aspects make every aspect/facet name a string literal; TypedQuery helpers only help with POCO aspects, and POCO aspects have no derived facets (see F1).");
findings.Observe("F12", "The archived secret still appears in default queries unless LifecycleState is set explicitly; hosts must remember to filter archived resources.");

var archivedVisible = await Query(new ResourceQuery { TenantScope = tenantA, DefinitionId = "Secret" });
Check(archivedVisible.Contains("sec-openai"), "Archived secret is expected to remain visible in an unfiltered query.");

findings.Print();
SqliteJsonCleanup.Delete(databasePath);
return;

// ---------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------

async Task<Resource> Create(string definitionId, TenantScope tenant, string resourceId, params (string Aspect, Dictionary<string, object> Facets)[] aspects) =>
    await manager.CreateAsync(definitionId, new CreateResourceRequest
    {
        TenantScope = tenant,
        ResourceId = resourceId,
        InitialAspects = aspects.ToDictionary(a => a.Aspect, a => (object)a.Facets),
    });

async Task<List<string>> Query(ResourceQuery query) =>
    (await queries.QueryAsync(query)).Select(resource => resource.ResourceId).Distinct().Order(StringComparer.Ordinal).ToList();

async Task<int?> ActiveVersion(string resourceId, string channel) =>
    (await manager.GetActiveVersionsAsync(resourceId, channel, tenantA, CancellationToken.None)).Select(r => (int?)r.Version).Max();

static async Task<Exception?> TrySave(Func<Task> save)
{
    try
    {
        await save();
        return null;
    }
    catch (Exception exception)
    {
        return exception;
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException($"Spike expectation failed: {message}");
}

static class Model
{
    public static ResourceDefinition Definition(string definitionId, params AspectDefinition[] aspects) => new()
    {
        DefinitionId = definitionId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        AspectDefinitions = aspects.ToDictionary(aspect => aspect.AspectDefinitionId),
    };

    public static AspectDefinition Aspect(string aspectId, params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = aspectId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        FacetDefinitions = facets,
    };

    public static FacetDefinition Facet(string facetId, string type, bool required = false, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = facetId,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        IsRequired = required,
        Metadata = metadata,
    };
}

sealed class ActivationLog
{
    public List<string> Events { get; } = [];
}

sealed class ActivationNotificationHook(ActivationLog log) : ResourceLifecycleHook
{
    public override ValueTask OnAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        log.Events.Add($"{context.TenantScope.TenantId}:{context.ResourceId}@v{context.Version}->{context.Channel}");
        return ValueTask.CompletedTask;
    }
}

sealed class Findings
{
    private readonly List<(string Id, string Text)> items = [];

    public void Observe(string id, string text) => items.Add((id, text));

    public void Print()
    {
        Console.WriteLine("Elsa spike findings");
        foreach (var (id, text) in items)
            Console.WriteLine($"  {id}: {text}");
    }
}

static class SqliteJsonCleanup
{
    public static void Delete(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
