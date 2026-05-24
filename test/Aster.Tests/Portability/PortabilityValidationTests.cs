using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Portability;

public sealed class PortabilityValidationTests
{
    [Fact]
    public async Task ValidateAsync_ActivationStateReferencesMissingResourceVersion_ReturnsError()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var result = await portability.ValidateAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            ActivationStates =
            [
                new ActivationState
                {
                    ResourceId = "missing-resource",
                    Channel = "Published",
                    ActiveVersions = [1],
                    LastUpdated = DateTime.UtcNow,
                },
            ],
        });

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.MissingResourceReference, diagnostic.Code);
        Assert.Equal(PortableDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task ValidateAsync_DuplicateDefinitionVersion_ReturnsDuplicateIdentityError()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var result = await portability.ValidateAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = "product-definition-v1",
                    Version = 1,
                },
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = "product-definition-v1-copy",
                    Version = 1,
                },
            ],
        });

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.DuplicateSnapshotIdentity, diagnostic.Code);
        Assert.Equal("""definitions/["Product",1]""", diagnostic.Path);
    }

    [Fact]
    public async Task ValidateAsync_MalformedResource_ReturnsMalformedSnapshotError()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var result = await portability.ValidateAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Resources =
            [
                new Resource
                {
                    ResourceId = "",
                    Id = "product-1-v1",
                    DefinitionId = "Product",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                },
            ],
        });

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.MalformedSnapshot, diagnostic.Code);
        Assert.Equal("resources/0/resourceId", diagnostic.Path);
    }

    [Fact]
    public async Task PreviewImportAsync_InvalidSourceTenantScope_ReturnsSingleSourceTenantDiagnostic()
    {
        await using var provider = new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();
        var portability = provider.GetRequiredService<IResourcePortabilityService>();

        var result = await portability.PreviewImportAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            SourceTenantScope = new TenantScope { TenantId = " " },
        });

        Assert.False(result.CanImport);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PortableDiagnosticCodes.InvalidTenantScope, diagnostic.Code);
        Assert.Equal("sourceTenantScope", diagnostic.Path);
    }
}
