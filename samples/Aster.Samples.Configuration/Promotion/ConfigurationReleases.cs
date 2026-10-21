using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Samples.Configuration.Model;

namespace Aster.Samples.Configuration.Promotion;

/// <summary>
/// The authoring side: saves configuration as immutable resource versions and moves versions
/// between environments. Every method is a thin layer over <see cref="IResourceManager"/>.
/// </summary>
public sealed class ConfigurationReleases(IResourceManager resources)
{
    /// <summary>
    /// Saves a feature flag. The first save creates version 1; later saves append a version.
    /// Saving activates nothing, so no environment changes until a version is released.
    /// </summary>
    public Task<Resource> SaveAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        return SaveAsync(ConfigurationDefinitions.FeatureFlagId, ConfigurationDefinitions.FeatureFlagResourceId(flag.Key), flag, cancellationToken);
    }

    /// <summary>Saves a settings section, as <see cref="SaveAsync(FeatureFlag, CancellationToken)"/>.</summary>
    public Task<Resource> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SaveAsync(ConfigurationDefinitions.AppSettingsId, ConfigurationDefinitions.AppSettingsResourceId(settings.Section), settings, cancellationToken);
    }

    private async Task<Resource> SaveAsync(string definitionId, string resourceId, object aspect, CancellationToken cancellationToken)
    {
        var latest = await resources.GetLatestVersionAsync(resourceId, cancellationToken);
        if (latest is null)
        {
            return await resources.CreateAsync(definitionId, new CreateResourceRequest
            {
                ResourceId = resourceId,
                InitialAspects = new() { [definitionId] = aspect },
            }, cancellationToken);
        }

        return await resources.UpdateAsync(resourceId, new UpdateResourceRequest
        {
            BaseVersion = latest.Version,
            AspectUpdates = new() { [definitionId] = aspect },
        }, cancellationToken);
    }

    /// <summary>
    /// Makes <paramref name="version"/> the one active version in <paramref name="environment"/>.
    /// Any version can be released, including an older one; that is how a rollback works.
    /// </summary>
    public async Task ReleaseAsync(string resourceId, int version, string environment, CancellationToken cancellationToken = default) =>
        await resources.ActivateAsync(resourceId, version, environment, allowMultipleActive: false, cancellationToken);

    /// <summary>
    /// Promotes the version active in <paramref name="fromEnvironment"/> to the next environment
    /// and returns it. Aster core deliberately has no promote call (Elsa spike finding F7), so this
    /// reads the active version in one channel and activates it in the next.
    /// </summary>
    /// <exception cref="InvalidOperationException">No version is active in <paramref name="fromEnvironment"/>.</exception>
    public async Task<int> PromoteAsync(string resourceId, string fromEnvironment, CancellationToken cancellationToken = default)
    {
        var toEnvironment = Environments.Next(fromEnvironment);
        var version = await GetActiveVersionAsync(resourceId, fromEnvironment, cancellationToken)
            ?? throw new InvalidOperationException($"No version of '{resourceId}' is active in '{fromEnvironment}'.");

        await ReleaseAsync(resourceId, version, toEnvironment, cancellationToken);
        return version;
    }

    /// <summary>
    /// Rolls <paramref name="environment"/> back to <paramref name="version"/>, an existing older
    /// version, through historical activation. Nothing is rewritten: the newer versions stay in the
    /// history and stay active wherever else they are active.
    /// </summary>
    /// <returns>The version that was active before the rollback.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not older than the active version.</exception>
    public async Task<int?> RollbackAsync(string resourceId, string environment, int version, CancellationToken cancellationToken = default)
    {
        var current = await GetActiveVersionAsync(resourceId, environment, cancellationToken);
        if (current is { } active && version >= active)
            throw new InvalidOperationException($"Version {version} of '{resourceId}' is not older than v{active}, the version active in '{environment}'.");

        await ReleaseAsync(resourceId, version, environment, cancellationToken);
        return current;
    }

    /// <summary>Returns the version active in <paramref name="environment"/>, or <see langword="null"/>.</summary>
    public async Task<int?> GetActiveVersionAsync(string resourceId, string environment, CancellationToken cancellationToken = default) =>
        (await resources.GetActiveVersionsAsync(resourceId, environment, cancellationToken))
            .Select(static resource => (int?)resource.Version)
            .Max();
}
