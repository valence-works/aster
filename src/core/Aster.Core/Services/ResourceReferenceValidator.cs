using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IResourceReferenceValidator"/>, built on bounded <see cref="IResourceVersionReader"/>
/// reads so every provider validates identically.
/// </summary>
public sealed class ResourceReferenceValidator(IResourceVersionReader versionReader) : IResourceReferenceValidator
{
    private const string LatestResolution = "latest";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FacetValueDiagnostic>> ValidateAsync(
        ResourceDefinition definition,
        IReadOnlyDictionary<string, object> aspects,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(aspects);

        var references = CollectReferences(definition, aspects);
        if (references.Count == 0)
            return [];

        var tenant = TenantScope.Resolve(tenantScope);
        var targetDefinitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var latest = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.Latest,
            ResourceIds = references.Select(static reference => reference.TargetResourceId).ToHashSet(StringComparer.Ordinal),
        }, cancellationToken);

        foreach (var version in latest)
            targetDefinitions.TryAdd(version.ResourceId, version.DefinitionId);

        var activeByChannel = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var channelReferences in references
                     .Where(reference => reference.Channel is not null && targetDefinitions.ContainsKey(reference.TargetResourceId))
                     .GroupBy(static reference => reference.Channel!, StringComparer.Ordinal))
        {
            var active = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                TenantScope = tenant,
                Scope = ResourceVersionScope.Active,
                ActivationChannel = channelReferences.Key,
                ResourceIds = channelReferences.Select(static reference => reference.TargetResourceId).ToHashSet(StringComparer.Ordinal),
            }, cancellationToken);

            activeByChannel[channelReferences.Key] = active.Select(static version => version.ResourceId).ToHashSet(StringComparer.Ordinal);
        }

        var diagnostics = new List<FacetValueDiagnostic>();
        foreach (var reference in references)
        {
            if (!targetDefinitions.TryGetValue(reference.TargetResourceId, out var targetDefinitionId))
            {
                diagnostics.Add(reference.Diagnostic(
                    FacetValueDiagnosticCodes.ReferenceTargetMissing,
                    $"Referenced resource '{reference.TargetResourceId}' does not exist."));
            }
            else if (reference.TargetDefinitionIds is { } allowed && !allowed.Contains(targetDefinitionId, StringComparer.Ordinal))
            {
                diagnostics.Add(reference.Diagnostic(
                    FacetValueDiagnosticCodes.ReferenceTargetTypeMismatch,
                    $"Referenced resource '{reference.TargetResourceId}' is a '{targetDefinitionId}'; allowed: {string.Join(", ", allowed.Select(static id => $"'{id}'"))}."));
            }
            else if (reference.Channel is { } channel && !activeByChannel[channel].Contains(reference.TargetResourceId))
            {
                diagnostics.Add(reference.Diagnostic(
                    FacetValueDiagnosticCodes.ReferencePinMissing,
                    $"Referenced resource '{reference.TargetResourceId}' has no version active in channel '{channel}'."));
            }
        }

        diagnostics.Sort(FacetValueValidator.CompareDiagnostics);
        return diagnostics;
    }

    private static List<ReferenceValue> CollectReferences(ResourceDefinition definition, IReadOnlyDictionary<string, object> aspects)
    {
        var references = new List<ReferenceValue>();

        foreach (var (aspectKey, aspectDefinition) in definition.AspectDefinitions)
        {
            if (!aspects.TryGetValue(aspectKey, out var payload) || payload is null)
                continue;

            foreach (var facet in aspectDefinition.FacetDefinitions)
            {
                var descriptor = ParseFacetType(facet.Type);
                if (descriptor.Element != FacetValueType.Reference
                    || !AspectFacetValueResolver.TryResolveValue(payload, facet.FacetDefinitionId, out var rawValue))
                {
                    continue;
                }

                var channel = facet.Metadata?.ReferenceResolution is { } resolution
                              && !string.Equals(resolution, LatestResolution, StringComparison.OrdinalIgnoreCase)
                    ? resolution
                    : null;

                void Add(object? value, int? elementIndex)
                {
                    // Ill-typed values are reported by FacetValueValidator; only well-typed ones are looked up.
                    if (TryNormalizeValue(value, FacetValueType.Reference, out var normalized))
                    {
                        references.Add(new ReferenceValue(
                            aspectKey, facet.FacetDefinitionId, elementIndex, (string)normalized, facet.Metadata?.TargetDefinitionIds, channel));
                    }
                }

                if (!descriptor.IsArray)
                {
                    Add(rawValue, null);
                }
                else if (TryGetArrayElements(rawValue, out var elements))
                {
                    for (var index = 0; index < elements.Count; index++)
                        Add(elements[index], index);
                }
            }
        }

        return references;
    }

    private sealed record ReferenceValue(
        string AspectKey,
        string FacetDefinitionId,
        int? ElementIndex,
        string TargetResourceId,
        IReadOnlyList<string>? TargetDefinitionIds,
        string? Channel)
    {
        public FacetValueDiagnostic Diagnostic(string code, string message) =>
            new(code, ElementIndex is { } index ? $"Element [{index}]: {message}" : message, AspectKey, FacetDefinitionId)
            {
                ElementIndex = ElementIndex,
            };
    }
}
