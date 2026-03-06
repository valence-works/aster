namespace Aster.Core.Models.Instances;

/// <summary>
/// Specifies the activation policy for a channel.
/// </summary>
/// <remarks>
/// Set on first activation of a channel; stored durably per <c>(ResourceId, Channel)</c> pair.
/// Once set, subsequent activations in the same channel reuse the stored mode unless an explicit
/// mode is supplied (which updates the stored value).
/// </remarks>
public enum ChannelMode
{
    /// <summary>
    /// At most one version may be active in the channel at any time.
    /// Activating a new version implicitly deactivates the previous one.
    /// </summary>
    SingleActive,

    /// <summary>
    /// Multiple versions may be active in the channel simultaneously.
    /// Activating a new version does not affect existing active versions.
    /// </summary>
    MultiActive,
}
