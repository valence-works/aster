namespace Aster.Core.Models.Instances;

/// <summary>
/// Tracks which resource versions are active in which channels.
/// </summary>
public sealed record ActivationState
{
    /// <summary>
    /// FK to <c>Resource.ResourceId</c> (logical identifier).
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Channel name (e.g., "Published", "Preview").
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Durable per-channel activation policy.
    /// Set on first activation; subsequent calls may update or reuse the stored value.
    /// </summary>
    public required ChannelMode Mode { get; init; }

    /// <summary>
    /// Active <c>Resource.Version</c> numbers. Supports multi-active.
    /// </summary>
    public IReadOnlyList<int> ActiveVersions { get; init; } = [];

    /// <summary>
    /// Timestamp of the most recent activation change.
    /// </summary>
    public required DateTime LastUpdated { get; init; }
}
