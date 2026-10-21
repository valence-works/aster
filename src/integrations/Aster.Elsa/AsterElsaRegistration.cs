using Aster.Elsa.Activities;
using Elsa.Workflows.Management.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa;

/// <summary>
/// The one registration both Elsa feature systems call (<see cref="Features.AsterFeature"/> and
/// <see cref="ShellFeatures.AsterFeature"/>), so they can't drift. It registers the activities only: the host registers
/// Aster itself (<c>AddAsterCore</c> and its provider).
/// </summary>
internal static class AsterElsaRegistration
{
    /// <summary>The activity types the module registers with Elsa's activity registry.</summary>
    public static IReadOnlyList<Type> ActivityTypes { get; } =
    [
        typeof(CreateResource),
        typeof(UpdateResource),
        typeof(GetResource),
        typeof(QueryResources),
        typeof(ActivateResourceVersion),
        typeof(DeactivateResourceVersion),
        typeof(PromoteResource),
        typeof(ArchiveResource),
    ];

    public static IServiceCollection Register(IServiceCollection services) => services.AddActivities(ActivityTypes);
}
