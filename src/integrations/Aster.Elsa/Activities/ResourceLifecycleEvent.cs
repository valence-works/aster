using Aster.Core.Models.Lifecycle;

namespace Aster.Elsa.Activities;

/// <summary>
/// The Aster change that started or resumed a workflow: the result of the Resource Saved, Resource Activated,
/// Resource Deactivated and Resource Archived triggers.
/// </summary>
public sealed record ResourceLifecycleEvent
{
    /// <summary>
    /// The workflow input key the event travels under, from the stimulus to the trigger that receives it.
    /// </summary>
    public const string WorkflowInputKey = "Aster.ResourceLifecycleEvent";

    /// <summary>Gets what happened to the resource.</summary>
    public ResourceLifecycleEventKind Kind { get; init; }

    /// <summary>Gets the Elsa tenant the change was made in: <c>""</c> for Elsa's default tenant (see <see cref="AsterTenantMapping"/>).</summary>
    public string TenantId { get; init; } = "";

    /// <summary>Gets the resource ID.</summary>
    public string ResourceId { get; init; } = "";

    /// <summary>Gets the definition of the resource, such as <c>Product</c>.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>
    /// Gets the version saved, activated or deactivated; <see langword="null"/> for an archive, which applies to the
    /// resource as a whole.
    /// </summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Gets the channel a version was activated in or deactivated from; otherwise <see langword="null"/>.</summary>
    public string? Channel { get; init; }

    /// <summary>Gets whether a save created the resource, updated it or upgraded its schema; otherwise <see langword="null"/>.</summary>
    public ResourceSaveKind? SaveKind { get; init; }

    /// <summary>Gets the ID of the Aster operation, as its lifecycle hooks saw it.</summary>
    public Guid OperationId { get; init; }
}

/// <summary>What happened to a resource in a <see cref="ResourceLifecycleEvent"/>.</summary>
public enum ResourceLifecycleEventKind
{
    /// <summary>A version was saved: the resource was created, updated or upgraded.</summary>
    Saved,

    /// <summary>A version was activated in a channel.</summary>
    Activated,

    /// <summary>A version was deactivated from a channel.</summary>
    Deactivated,

    /// <summary>The resource was archived.</summary>
    Archived,
}

/// <summary>
/// The stimulus Aster sends to Elsa for a lifecycle event, and the payload the triggers index and bookmark. Elsa hashes
/// it with the trigger's activity type name, so a workflow starts or resumes only when every value matches its
/// trigger's: the tenant, and the definition ID and channel filters, where <see langword="null"/> stands for an empty
/// filter. Aster sends one stimulus for each combination of filled and empty filters, so a workflow matches exactly one
/// of them.
/// </summary>
/// <param name="TenantId">The Elsa tenant ID: <c>""</c> for Elsa's default tenant.</param>
/// <param name="DefinitionId">The definition ID filter, or <see langword="null"/> for any definition.</param>
/// <param name="Channel">The channel filter of the activation triggers, or <see langword="null"/> for any channel.</param>
public sealed record ResourceLifecycleStimulus(string TenantId, string? DefinitionId = null, string? Channel = null);
