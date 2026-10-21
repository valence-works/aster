using System.Runtime.CompilerServices;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Fires when a resource version is activated in a channel. Filters: definition ID and channel.
/// </summary>
[Activity("Aster", "Aster", "Starts or resumes the workflow when an Aster resource version is activated in a channel.", DisplayName = "Resource Activated")]
public sealed class ResourceActivated : ResourceLifecycleTrigger
{
    /// <summary>Initializes a new instance of the <see cref="ResourceActivated"/> trigger.</summary>
    /// <param name="source">The source file of a code-first workflow; set by the compiler.</param>
    /// <param name="line">The source line of a code-first workflow; set by the compiler.</param>
    public ResourceActivated([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>Gets or sets the channel filter, such as <c>Published</c>. Empty matches every channel.</summary>
    [Input(Description = "Only activations in this channel, such as Published. Leave empty for every channel.")]
    public Input<string?>? Channel { get; set; }

    /// <inheritdoc />
    private protected override Input<string?>? ChannelFilter => Channel;
}
