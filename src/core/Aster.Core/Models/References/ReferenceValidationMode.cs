namespace Aster.Core.Models.References;

/// <summary>
/// How resource saves treat reference values whose targets fail validation
/// (<see cref="Definitions.FacetValueDiagnosticCodes.ReferenceTargetMissing"/>,
/// <see cref="Definitions.FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch"/>,
/// <see cref="Definitions.FacetValueDiagnosticCodes.ReferencePinMissing"/>).
/// </summary>
public enum ReferenceValidationMode
{
    /// <summary>
    /// The save is rejected with a <see cref="Exceptions.FacetValueValidationException"/>; nothing is persisted.
    /// </summary>
    Strict,

    /// <summary>
    /// The save proceeds. The diagnostics are logged as warnings and passed to after-save lifecycle
    /// hooks as <see cref="Lifecycle.ResourceSaveLifecycleContext.ReferenceWarnings"/>.
    /// </summary>
    Lenient,
}
