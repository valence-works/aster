using System.Runtime.CompilerServices;
using Elsa.Workflows.Attributes;

namespace Aster.Elsa.Activities;

/// <summary>
/// Fires when a resource version is saved: created, updated or schema-upgraded. Filters: definition ID. The result's
/// <see cref="ResourceLifecycleEvent.ResourceVersion"/> is the saved version and
/// <see cref="ResourceLifecycleEvent.SaveKind"/> says how it was saved.
/// </summary>
[Activity("Aster", "Aster", "Starts or resumes the workflow when a version of an Aster resource is saved: created, updated or upgraded.", DisplayName = "Resource Saved")]
public sealed class ResourceSaved : ResourceLifecycleTrigger
{
    /// <summary>Initializes a new instance of the <see cref="ResourceSaved"/> trigger.</summary>
    /// <param name="source">The source file of a code-first workflow; set by the compiler.</param>
    /// <param name="line">The source line of a code-first workflow; set by the compiler.</param>
    public ResourceSaved([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }
}
