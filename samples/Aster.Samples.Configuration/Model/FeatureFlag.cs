namespace Aster.Samples.Configuration.Model;

/// <summary>
/// The single aspect of a <c>FeatureFlag</c> resource. Its facets are derived from these properties:
/// <c>Key</c> (string, required), <c>Description</c> (string), <c>Enabled</c> (bool),
/// <c>RolloutPercentage</c> (int) and <c>AllowedTenants</c> (string[]).
/// </summary>
public sealed record FeatureFlag
{
    /// <summary>The flag key the application asks for, such as <c>new-checkout</c>.</summary>
    public required string Key { get; init; }

    /// <summary>What the flag controls, for the people who change it.</summary>
    public string? Description { get; init; }

    /// <summary>The kill switch: when <see langword="false"/>, the flag is off for everyone.</summary>
    public bool Enabled { get; init; }

    /// <summary>The share of subjects (0–100) that see the feature once the tenant check passes.</summary>
    public int RolloutPercentage { get; init; } = 100;

    /// <summary>The application tenants the flag is limited to; empty means every tenant.</summary>
    public string[] AllowedTenants { get; init; } = [];
}
