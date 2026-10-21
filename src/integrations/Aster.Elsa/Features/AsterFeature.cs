using Elsa.Features.Abstractions;
using Elsa.Features.Attributes;
using Elsa.Features.Services;
using Elsa.Workflows.Management.Features;
using Elsa.Workflows.Runtime.Features;

namespace Aster.Elsa.Features;

/// <summary>
/// The Aster feature for Elsa's classic feature system (<c>AddElsa(elsa => elsa.UseAster())</c>): registers the Aster
/// activities. The host registers Aster itself with <c>AddAsterCore()</c> and the provider that matches its Elsa
/// database. CShells hosts use <see cref="ShellFeatures.AsterFeature"/>, which registers the same services.
/// </summary>
[DependsOn(typeof(WorkflowManagementFeature))]
[DependsOn(typeof(WorkflowRuntimeFeature))]
public class AsterFeature(IModule module) : FeatureBase(module)
{
    /// <inheritdoc />
    public override void Apply() => AsterElsaRegistration.Register(Services);
}
