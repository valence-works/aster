namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The activation channels of the registry. A saved version that is active in no channel is a draft.
/// </summary>
public static class Channels
{
    /// <summary>Where a version is evaluated before it serves traffic.</summary>
    public const string Candidate = "candidate";

    /// <summary>What serves traffic. It may hold two active versions of a prompt during an A/B test.</summary>
    public const string Production = "production";
}
