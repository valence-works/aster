using Aster.Core.InMemory;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Tests.Querying;

public sealed class TenantQueryValidatorTests
{
    private readonly ResourceQueryValidator validator = new([new InMemoryQueryCapabilitiesProvider()]);

    [Fact]
    public void Validate_BlankTenantScope_ReturnsTenantScopeFailureBeforeProviderTraversal()
    {
        var result = validator.Validate(new ResourceQuery
        {
            TenantScope = new TenantScope { TenantId = " " },
        });

        var failure = Assert.Single(result.Failures);
        Assert.Equal("tenant-scope-invalid", failure.Code);
        Assert.Equal("TenantScope", failure.Path);
    }
}
