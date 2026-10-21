using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Definitions;

namespace Aster.Samples.Configuration.Model;

/// <summary>
/// The two resource definitions this sample stores configuration in.
/// </summary>
public static class ConfigurationDefinitions
{
    /// <summary>Definition ID of feature flags, and the key of their single aspect.</summary>
    public const string FeatureFlagId = nameof(FeatureFlag);

    /// <summary>Definition ID of settings sections, and the key of their single aspect.</summary>
    public const string AppSettingsId = nameof(AppSettings);

    /// <summary>The definition IDs whose activations change the configuration an application sees.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { FeatureFlagId, AppSettingsId };

    /// <summary>
    /// A feature flag. Facets are derived from <see cref="FeatureFlag"/>; metadata adds constraints
    /// that Aster enforces on every save.
    /// </summary>
    public static ResourceDefinition FeatureFlagDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(FeatureFlagId)
        .WithAspect<FeatureFlag>()
        .WithFacetMetadata(nameof(FeatureFlag.Key), new FacetMetadata { Pattern = "^[a-z0-9][a-z0-9-]*$", MaxLength = 64 })
        .WithFacetMetadata(nameof(FeatureFlag.RolloutPercentage), new FacetMetadata { Minimum = 0, Maximum = 100 })
        .WithFacetMetadata(nameof(FeatureFlag.AllowedTenants), new FacetMetadata { UniqueItems = true })
        .Build();

    /// <summary>
    /// A settings section. <see cref="AppSettings.Values"/> derives an <c>object</c> facet, so a
    /// section can hold any JSON object. The section name pattern reserves <c>FeatureFlags</c>.
    /// </summary>
    public static ResourceDefinition AppSettingsDefinition() => new ResourceDefinitionBuilder()
        .WithDefinitionId(AppSettingsId)
        .WithAspect<AppSettings>()
        .WithFacetMetadata(nameof(AppSettings.Section), new FacetMetadata { Pattern = "^(?!(?i:featureflags)$)[A-Za-z][A-Za-z0-9]*$" })
        .Build();

    /// <summary>Registers both definitions.</summary>
    public static async Task RegisterAsync(IResourceDefinitionStore definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        await definitions.RegisterDefinitionAsync(FeatureFlagDefinition(), cancellationToken);
        await definitions.RegisterDefinitionAsync(AppSettingsDefinition(), cancellationToken);
    }

    /// <summary>The resource ID a feature flag is stored under.</summary>
    public static string FeatureFlagResourceId(string key) => $"feature-flag.{key}";

    /// <summary>The resource ID a settings section is stored under.</summary>
    public static string AppSettingsResourceId(string section) => $"app-settings.{section.ToLowerInvariant()}";
}
