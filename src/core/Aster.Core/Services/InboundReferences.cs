using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

namespace Aster.Core.Services;

/// <summary>
/// Finds which resources reference given targets, for lifecycle previews (#90). Referrers are the
/// latest versions in the tenant, read through the reference facets of each definition's latest version.
/// </summary>
internal static class InboundReferences
{
    /// <summary>
    /// Maps each target that has inbound references to its referrers' <c>ResourceId</c>s, ordered
    /// ordinally. A resource referencing itself is not its own referrer.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Find(
        IEnumerable<Resource> latestVersions,
        IEnumerable<ResourceDefinition> definitions,
        IReadOnlySet<string> targetResourceIds)
    {
        if (targetResourceIds.Count == 0)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var facetsByDefinition = definitions
            .GroupBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => ReferenceFacetValues.Facets(group.MaxBy(static definition => definition.Version)!),
                StringComparer.Ordinal);

        var referrers = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var version in latestVersions)
        {
            if (!facetsByDefinition.TryGetValue(version.DefinitionId, out var facets) || facets.Count == 0)
                continue;

            foreach (var value in ReferenceFacetValues.Read(facets, version.Aspects))
            {
                if (!targetResourceIds.Contains(value.TargetResourceId)
                    || string.Equals(value.TargetResourceId, version.ResourceId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!referrers.TryGetValue(value.TargetResourceId, out var set))
                    referrers[value.TargetResourceId] = set = new SortedSet<string>(StringComparer.Ordinal);

                set.Add(version.ResourceId);
            }
        }

        return referrers.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToList(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// One <see cref="ResourcePolicyDiagnosticCodes.HasInboundReferences"/> diagnostic per referrer, with
    /// <c>Path</c> <c>referrers/{referrerId}</c>.
    /// </summary>
    public static IEnumerable<ResourcePolicyDiagnostic> Diagnostics(
        string resourceId,
        IReadOnlyList<string> referrers,
        string? policyId = null,
        int? resourceVersion = null) =>
        referrers.Select(referrer => new ResourcePolicyDiagnostic
        {
            Code = ResourcePolicyDiagnosticCodes.HasInboundReferences,
            Message = $"Resource '{resourceId}' is referenced by '{referrer}'.",
            Path = $"referrers/{referrer}",
            PolicyId = policyId,
            ResourceId = resourceId,
            ResourceVersion = resourceVersion,
        });
}
