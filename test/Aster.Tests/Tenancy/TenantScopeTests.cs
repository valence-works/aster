using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Tests.Tenancy;

public sealed class TenantScopeTests
{
    [Fact]
    public void Resolve_NullScope_ReturnsDefaultTenant()
    {
        var scope = TenantScopeResolver.Resolve(null);

        Assert.Equal(TenantScope.DefaultTenantId, scope.TenantId);
        Assert.True(scope.IsDefault);
    }

    [Fact]
    public void FromTenantId_PreservesOpaqueExactTenantId()
    {
        var lower = TenantScope.FromTenantId("tenant-a");
        var upper = TenantScope.FromTenantId("Tenant-A");

        Assert.Equal("tenant-a", lower.TenantId);
        Assert.NotEqual(lower, upper);
    }
}
