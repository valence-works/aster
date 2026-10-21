using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Samples.Configuration.Model;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// Reads the configuration active in one channel and flattens it into configuration keys:
/// <list type="bullet">
///   <item>each feature flag becomes <c>FeatureFlags:{Key}:Enabled</c>, <c>:RolloutPercentage</c> and <c>:AllowedTenants:{index}</c>;</item>
///   <item>each settings section becomes <c>{Section}:{path}</c>, with nested objects and arrays as nested keys.</item>
/// </list>
/// </summary>
public sealed class ChannelConfigurationLoader(IResourceQueryService queries, ITypedAspectBinder binder)
{
    /// <summary>The configuration section feature flags appear under.</summary>
    public const string FeatureFlagsSection = "FeatureFlags";

    /// <summary>Loads the configuration active in <paramref name="channel"/>.</summary>
    public async Task<Dictionary<string, string?>> LoadAsync(string channel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in await ActiveAsync(ConfigurationDefinitions.FeatureFlagId, channel, cancellationToken))
        {
            var flag = resource.GetAspect<FeatureFlag>(ConfigurationDefinitions.FeatureFlagId, binder)!;
            var prefix = ConfigurationPath.Combine(FeatureFlagsSection, flag.Key);
            data[ConfigurationPath.Combine(prefix, nameof(FeatureFlag.Enabled))] = flag.Enabled ? "true" : "false";
            data[ConfigurationPath.Combine(prefix, nameof(FeatureFlag.RolloutPercentage))] = flag.RolloutPercentage.ToString(CultureInfo.InvariantCulture);
            for (var index = 0; index < flag.AllowedTenants.Length; index++)
                data[ConfigurationPath.Combine(prefix, nameof(FeatureFlag.AllowedTenants), index.ToString(CultureInfo.InvariantCulture))] = flag.AllowedTenants[index];
        }

        foreach (var resource in await ActiveAsync(ConfigurationDefinitions.AppSettingsId, channel, cancellationToken))
        {
            var settings = resource.GetAspect<AppSettings>(ConfigurationDefinitions.AppSettingsId, binder)!;
            Flatten(settings.Values, settings.Section, data);
        }

        return data;
    }

    // One resource per configuration item; the channel is single-active, so take the highest version defensively.
    private async Task<IEnumerable<Resource>> ActiveAsync(string definitionId, string channel, CancellationToken cancellationToken) =>
        (await queries.QueryAsync(new ResourceQuery
        {
            DefinitionId = definitionId,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = channel,
        }, cancellationToken))
        .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
        .Select(static versions => versions.MaxBy(static resource => resource.Version)!)
        .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal);

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string?> data)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                    Flatten(child, ConfigurationPath.Combine(path, name), data);
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    Flatten(array[index], ConfigurationPath.Combine(path, index.ToString(CultureInfo.InvariantCulture)), data);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                data[path] = value.GetValue<string>();
                break;
            case JsonValue value:
                data[path] = value.ToJsonString();
                break;
            default:
                data[path] = null;
                break;
        }
    }
}
