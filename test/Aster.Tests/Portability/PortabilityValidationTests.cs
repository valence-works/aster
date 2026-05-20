using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
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
}
