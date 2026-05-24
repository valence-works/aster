using Aster.Core.Exceptions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Tests.Tenancy;

public sealed class TenantScopeFailureTests
{
    [Fact]
    public void FromTenantId_BlankTenantId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TenantScope.FromTenantId(" "));
    }

    [Fact]
    public void Resolve_BlankTenantId_ThrowsStableTenantScopeException()
    {
        var exception = Assert.Throws<TenantScopeException>(() =>
            TenantScopeResolver.Resolve(new TenantScope { TenantId = " " }));

        Assert.Equal(TenantScopeException.InvalidCode, exception.Code);
    }
}
