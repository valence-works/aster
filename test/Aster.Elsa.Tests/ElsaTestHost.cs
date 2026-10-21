using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Elsa.Common.Multitenancy;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Tests;

/// <summary>
/// An in-process Elsa host with the Aster module on an Aster provider, running code-first workflows through Elsa's
/// <see cref="IWorkflowRunner"/>.
/// </summary>
internal sealed class ElsaTestHost : IAsyncDisposable
{
    public const string InMemory = "in-memory";
    public const string Sqlite = "sqlite";

    private readonly ServiceProvider services;
    private readonly string? database;

    private ElsaTestHost(ServiceProvider services, string? database)
    {
        this.services = services;
        this.database = database;
    }

    public IServiceProvider Services => services;

    public IResourceManager Manager => services.GetRequiredService<IResourceManager>();

    /// <summary>Starts a host with the <c>Product</c> definition registered in the given Aster tenants (default: the default tenant).</summary>
    public static async Task<ElsaTestHost> StartAsync(string provider = InMemory, params TenantScope[] tenants)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAsterCore();
        string? database = null;
        if (provider == Sqlite)
        {
            database = Path.Combine(Path.GetTempPath(), $"aster-elsa-{Guid.NewGuid():N}.db");
            collection.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={database};Pooling=False");
        }

        collection.AddElsa(elsa => elsa.UseAster());

        var host = new ElsaTestHost(collection.BuildServiceProvider(), database);
        var store = host.services.GetRequiredService<IResourceDefinitionStore>();
        foreach (var tenant in tenants.Length == 0 ? [TenantScope.Default] : tenants)
            await store.RegisterDefinitionAsync(Product() with { TenantScope = tenant }, tenant, CancellationToken.None);

        return host;
    }

    /// <summary>A product with a required <c>Title</c> of at most 20 characters and an optional <c>Price</c>.</summary>
    public static ResourceDefinition Product() => new ResourceDefinitionBuilder()
        .WithDefinitionId("Product")
        .WithAspect("Title")
            .WithFacet("Title", "string", isRequired: true)
            .WithFacetMetadata(new FacetMetadata { Label = "Title", MaxLength = 20 })
        .WithAspect("Price")
            .WithFacet("Amount", "decimal")
        .Build();

    /// <summary>Runs <paramref name="activities"/> in sequence, in the Elsa tenant <paramref name="elsaTenantId"/> when given.</summary>
    public async Task<RunWorkflowResult> RunAsync(string? elsaTenantId, params IActivity[] activities)
    {
        using var scope = services.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantAccessor>();
        using var tenant = elsaTenantId is null ? null : tenants.PushContext(new Tenant { Id = elsaTenantId, Name = elsaTenantId });

        var root = new Sequence();
        foreach (var activity in activities)
            root.Activities.Add(activity);

        // Declare every variable the activities bind to, so their inputs and outputs resolve.
        foreach (var variable in activities.SelectMany(Variables).Distinct())
            root.Variables.Add(variable);

        return await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().RunAsync(new Workflow { Root = root });
    }

    private static IEnumerable<Variable> Variables(IActivity activity) =>
        activity.GetType().GetProperties()
            .Where(static property => typeof(Argument).IsAssignableFrom(property.PropertyType))
            .Select(property => (property.GetValue(activity) as Argument)?.MemoryBlockReference())
            .OfType<Variable>();

    /// <summary>Runs a code-first workflow in Elsa's default tenant.</summary>
    public async Task<RunWorkflowResult> RunWorkflowAsync<TWorkflow>() where TWorkflow : IWorkflow, new()
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().RunAsync<TWorkflow>();
    }

    /// <summary>Runs <paramref name="activities"/> in sequence in Elsa's default tenant.</summary>
    public Task<RunWorkflowResult> RunAsync(params IActivity[] activities) => RunAsync(null, activities);

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        if (database is not null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }
}

/// <summary>Captures a value in a workflow, such as an activity's result, for the test to assert on.</summary>
public sealed class Capture : CodeActivity
{
    private readonly List<object?> sink;

    public Capture(List<object?> sink, Input<object?> value)
    {
        this.sink = sink;
        Value = value;
    }

    public Input<object?> Value { get; set; }

    protected override void Execute(ActivityExecutionContext context) => sink.Add(context.Get(Value));
}

internal static class RunWorkflowResultExtensions
{
    public static void AssertCompleted(this RunWorkflowResult result)
    {
        var incidents = string.Join("; ", result.WorkflowState.Incidents.Select(static incident => $"{incident.ActivityType}: {incident.Message}"));
        Assert.True(result.WorkflowState.SubStatus == WorkflowSubStatus.Finished, $"The workflow ended {result.WorkflowState.SubStatus}: {incidents}");
    }

    /// <summary>Asserts the workflow faulted on one Aster activity and returns the fault's code.</summary>
    public static string FaultCode(this RunWorkflowResult result)
    {
        Assert.Equal(WorkflowSubStatus.Faulted, result.WorkflowState.SubStatus);
        var incident = Assert.Single(result.WorkflowState.Incidents);
        Assert.Equal(typeof(AsterActivityException), incident.Exception!.Type);
        return incident.Exception.Metadata!["code"];
    }

    /// <summary>The diagnostics of the fault, as the incident metadata carries them.</summary>
    public static JsonElement FaultDiagnostics(this RunWorkflowResult result) =>
        JsonDocument.Parse(Assert.Single(result.WorkflowState.Incidents).Exception!.Metadata!["diagnostics"]).RootElement;
}
