namespace Aster.Core.Models.Policies;

/// <summary>
/// The versions a prune must leave behind: at least <see cref="MinimumRemainingVersions"/>, counting every version
/// or only versions that aren't active on any channel. The pruning store checks it atomically with the delete, so
/// concurrent prunes of one resource can't together go below it.
/// </summary>
/// <param name="MinimumRemainingVersions">The fewest versions that may remain once the target is removed.</param>
/// <param name="InactiveVersionsOnly">Whether only versions not active on any channel count toward the floor.</param>
public sealed record ResourceVersionPruneFloor(int MinimumRemainingVersions, bool InactiveVersionsOnly);
