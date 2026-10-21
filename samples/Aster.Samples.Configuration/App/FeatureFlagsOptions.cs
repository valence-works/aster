namespace Aster.Samples.Configuration.App;

/// <summary>
/// The application's view of its feature flags, bound from the <c>FeatureFlags</c> section and keyed
/// by flag key.
/// </summary>
public sealed class FeatureFlagsOptions() : Dictionary<string, FeatureFlagSettings>(StringComparer.OrdinalIgnoreCase);

/// <summary>One feature flag as the application reads it.</summary>
public sealed class FeatureFlagSettings
{
    /// <summary>Whether the flag is on at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>The share of subjects (0–100) the flag is on for.</summary>
    public int RolloutPercentage { get; set; } = 100;

    /// <summary>The tenants the flag is limited to; empty means every tenant.</summary>
    public string[] AllowedTenants { get; set; } = [];
}
