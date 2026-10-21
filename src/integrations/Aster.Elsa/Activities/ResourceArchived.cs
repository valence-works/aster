using System.Runtime.CompilerServices;
using Elsa.Workflows.Attributes;

namespace Aster.Elsa.Activities;

/// <summary>
/// Fires when a resource is archived, by the Archive Resource activity, the lifecycle marker service or policy
/// application. Soft-deleting a resource does not fire it. Filters: definition ID.
/// </summary>
[Activity("Aster", "Aster", "Starts or resumes the workflow when an Aster resource is archived.", DisplayName = "Resource Archived")]
public sealed class ResourceArchived : ResourceLifecycleTrigger
{
    /// <summary>Initializes a new instance of the <see cref="ResourceArchived"/> trigger.</summary>
    /// <param name="source">The source file of a code-first workflow; set by the compiler.</param>
    /// <param name="line">The source line of a code-first workflow; set by the compiler.</param>
    public ResourceArchived([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }
}
