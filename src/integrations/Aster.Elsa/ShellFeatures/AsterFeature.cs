using CShells.Features;
using Elsa.Workflows.Management.ShellFeatures;
using Elsa.Workflows.Runtime.ShellFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.ShellFeatures;

/// <summary>
/// The Aster feature for CShells shell-based Elsa hosts: registers the Aster activities, triggers and trigger hook, as
/// <see cref="Features.AsterFeature"/> does for Elsa's classic feature system. The host registers Aster itself with
/// <c>AddAsterCore()</c> and the provider that matches its Elsa database.
/// </summary>
[ShellFeature(
    "Aster",
    DisplayName = "Aster",
    Description = "Activities over Aster resources (create, update, get, query, activate, deactivate, promote and archive) and triggers on their saves, activations, deactivations and archives",
    DependsOn = [typeof(WorkflowManagementFeature), typeof(WorkflowRuntimeFeature)])]
public class AsterFeature : IShellFeature
{
    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services) => AsterElsaRegistration.Register(services);
}
