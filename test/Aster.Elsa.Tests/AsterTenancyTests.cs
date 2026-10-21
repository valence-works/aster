using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Elsa.Activities;
using Elsa.Common.Multitenancy;
using Elsa.Workflows.Memory;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Tests;

/// <summary>
/// Elsa's current tenant maps to an Aster tenant scope on every call: <c>""</c> is the default scope, any other ID the
/// same ID, and <c>*</c>, <c>default</c> and blank or whitespace-padded IDs are refused.
/// </summary>
public sealed class AsterTenancyTests
{
    private const string Gadget = """{ "Title": { "Title": "Gadget" } }""";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void The_default_Elsa_tenant_maps_to_the_default_scope(string? elsaTenantId)
    {
        Assert.Equal(TenantScope.Default, AsterTenantMapping.FromElsaTenantId(elsaTenantId));
    }

    [Fact]
    public void Any_other_Elsa_tenant_maps_to_the_same_tenant_id()
    {
        Assert.Equal(TenantScope.FromTenantId("Tenant-A"), AsterTenantMapping.FromElsaTenantId("Tenant-A"));
    }

    [Theory]
    [InlineData(Tenant.AgnosticTenantId)]
    [InlineData(TenantScope.DefaultTenantId)]
    [InlineData("  ")]
    [InlineData(" tenant-a")]
    [InlineData("tenant-a\t")]
    public void The_agnostic_default_named_blank_and_padded_Elsa_tenants_are_refused(string elsaTenantId)
    {
        var exception = Assert.Throws<TenantScopeException>(() => AsterTenantMapping.FromElsaTenantId(elsaTenantId));

        Assert.Equal(TenantScopeException.InvalidCode, exception.Code);
        Assert.Equal(elsaTenantId, exception.TenantId);
    }

    [Fact]
    public void Without_a_tenant_accessor_every_call_uses_the_default_scope()
    {
        Assert.Equal(TenantScope.Default, AsterTenantMapping.FromTenantAccessor(null));
    }

    [Theory]
    [InlineData(ElsaTestHost.InMemory)]
    [InlineData(ElsaTestHost.Sqlite)]
    public async Task Activities_read_and_write_in_the_Aster_tenant_of_the_current_Elsa_tenant(string provider)
    {
        var tenantA = TenantScope.FromTenantId("tenant-a");
        var tenantB = TenantScope.FromTenantId("tenant-b");
        await using var host = await ElsaTestHost.StartAsync(provider, TenantScope.Default, tenantA, tenantB);

        var result = await host.RunAsync("tenant-a", new CreateResource { DefinitionId = new("Product"), ResourceId = new("gadget"), Aspects = new(Gadget) });

        result.AssertCompleted();
        Assert.Equal(tenantA, (await host.Manager.GetLatestVersionAsync("gadget", tenantA, CancellationToken.None))!.TenantScope);
        Assert.Null(await host.Manager.GetLatestVersionAsync("gadget", tenantB, CancellationToken.None));
        Assert.Null(await host.Manager.GetLatestVersionAsync("gadget"));

        // The same host resolves the tenant per call: the default Elsa tenant and tenant-b see nothing.
        var captured = new List<object?>();
        var fromDefault = new Variable<IReadOnlyList<Resource>>();
        result = await host.RunAsync(new QueryResources { DefinitionId = new("Product"), Result = new(fromDefault) }, new Capture(captured, new(fromDefault)));
        result.AssertCompleted();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Resource>>(Assert.Single(captured)));

        Assert.Equal(
            AsterActivityFaultCodes.ResourceNotFound,
            (await host.RunAsync("tenant-b", new UpdateResource { ResourceId = new("gadget"), BaseVersion = new(1), Aspects = new(Gadget) })).FaultCode());

        result = await host.RunAsync("tenant-a", new UpdateResource { ResourceId = new("gadget"), BaseVersion = new(1), Aspects = new(Gadget) });
        result.AssertCompleted();
        Assert.Equal(2, (await host.Manager.GetLatestVersionAsync("gadget", tenantA, CancellationToken.None))!.Version);
    }

    [Theory]
    [InlineData(Tenant.AgnosticTenantId)]
    [InlineData(TenantScope.DefaultTenantId)]
    [InlineData(" tenant-a")]
    public async Task Activities_in_a_refused_Elsa_tenant_fault_with_tenant_scope_invalid_and_write_nothing(string elsaTenantId)
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunAsync(elsaTenantId, new CreateResource { DefinitionId = new("Product"), ResourceId = new("gadget"), Aspects = new(Gadget) });

        Assert.Equal(TenantScopeException.InvalidCode, result.FaultCode());
        Assert.Null(await host.Manager.GetLatestVersionAsync("gadget"));
    }
}
