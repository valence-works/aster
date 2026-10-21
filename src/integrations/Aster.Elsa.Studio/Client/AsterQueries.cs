using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Forms;

namespace Aster.Elsa.Studio.Client;

/// <summary>
/// Builds the bodies of <c>POST /resources/query</c> the module sends, in the wire format of
/// <c>Aster.AspNetCore.AsterQueryRequest</c>, and runs the lookups built on it: the reference picker's search and the
/// reverse reference lookup.
/// </summary>
internal static class AsterQueries
{
    /// <summary>The default page size of the reference picker's search.</summary>
    public const int PickerPageSize = 20;

    /// <summary>
    /// A search over resource IDs: <c>contains</c> when the provider declares it for <c>ResourceId</c>, else
    /// <c>startsWith</c> when declared, else no filter (the caller filters the page itself). Sorted by
    /// <c>ResourceId</c> when the provider sorts metadata.
    /// </summary>
    /// <returns>The query, and whether it filters by <paramref name="text"/> itself.</returns>
    public static (JsonObject Query, bool FiltersText) Search(
        string? definitionId, string? text, AsterQueryCapabilities? capabilities, int take, int skip = 0, string? lifecycle = null)
    {
        var query = new JsonObject { ["take"] = take };
        if (skip > 0)
            query["skip"] = skip;
        if (!string.IsNullOrEmpty(definitionId))
            query["definitionId"] = definitionId;
        if (!string.IsNullOrEmpty(lifecycle))
            query["lifecycle"] = lifecycle;
        if (capabilities?.SupportsMetadataSorting == true)
            query["sort"] = new JsonArray(new JsonObject { ["field"] = "ResourceId" });

        var filtersText = false;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var op = TextOperator(capabilities);
            if (op is not null)
            {
                query["filter"] = new JsonObject { ["field"] = "ResourceId", ["op"] = op, ["value"] = text.Trim() };
                filtersText = true;
            }
        }

        return (query, filtersText);
    }

    /// <summary>The operator <see cref="Search"/> uses on <c>ResourceId</c>, or <see langword="null"/> when the provider declares neither.</summary>
    public static string? TextOperator(AsterQueryCapabilities? capabilities)
    {
        if (capabilities is null)
            return null;
        if (capabilities.MetadataOperators.Contains("contains") && capabilities.MetadataContainsFields.Contains("resourceid"))
            return "contains";
        return capabilities.MetadataOperators.Contains("startswith") ? "startsWith" : null;
    }

    /// <summary>
    /// The reverse reference lookup over HTTP: the resources of <paramref name="definitionId"/> whose
    /// <paramref name="aspectKey"/>.<paramref name="facetId"/> names <paramref name="resourceId"/> (a <c>reference</c>
    /// facet equal to it or a <c>reference[]</c> facet containing it).
    /// </summary>
    public static JsonObject Referrers(string definitionId, string aspectKey, string facetId, string resourceId, int take) => new()
    {
        ["definitionId"] = definitionId,
        ["filter"] = new JsonObject { ["aspect"] = aspectKey, ["facet"] = facetId, ["references"] = resourceId },
        ["take"] = take,
    };

    /// <summary>
    /// Searches the resources a reference facet may point to: one query per target definition (or one over every
    /// definition when the facet names none), merged and ordered by resource ID.
    /// </summary>
    public static async Task<IReadOnlyList<AsterReferenceCandidate>> SearchReferenceCandidatesAsync(
        AsterStudioClient client,
        IReadOnlyList<string> targetDefinitionIds,
        string? text,
        int take = PickerPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(targetDefinitionIds);

        var capabilities = await client.GetQueryCapabilitiesAsync(cancellationToken);
        var candidates = new List<AsterReferenceCandidate>();
        List<string?> targets = targetDefinitionIds.Count == 0 ? [null] : [.. targetDefinitionIds];
        foreach (var target in targets)
        {
            var (query, filtersText) = Search(target, text, capabilities, take);
            var page = await client.QueryResourcesAsync(query, cancellationToken);
            candidates.AddRange(page.Items
                .Where(item => filtersText || string.IsNullOrWhiteSpace(text) || item.ResourceId.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(static item => new AsterReferenceCandidate(item.ResourceId, item.DefinitionId)));
        }

        return candidates
            .DistinctBy(static candidate => candidate.ResourceId, StringComparer.Ordinal)
            .OrderBy(static candidate => candidate.ResourceId, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Finds the resources that reference <paramref name="resourceId"/>: for every definition with a reference facet
    /// that may point to <paramref name="definitionId"/> (its schema's <c>x-aster-reference</c>), a query with a
    /// <c>references</c> filter on that facet.
    /// </summary>
    public static async Task<IReadOnlyList<AsterReferrer>> FindReferrersAsync(
        AsterStudioClient client,
        string resourceId,
        string definitionId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var found = new Dictionary<string, (string DefinitionId, List<string> Via)>(StringComparer.Ordinal);
        foreach (var definition in await client.ListDefinitionsAsync(cancellationToken))
        {
            var model = AsterFormModel.FromSchema(await client.GetDefinitionSchemaAsync(definition.DefinitionId, cancellationToken: cancellationToken));
            var facets = model.Fields.Where(field =>
                field.Kind == AsterValueKind.Reference
                && (field.ReferenceTargets.Count == 0 || field.ReferenceTargets.Contains(definitionId, StringComparer.Ordinal)));

            foreach (var field in facets)
            {
                var page = await client.QueryResourcesAsync(
                    Referrers(definition.DefinitionId, field.AspectKey, field.FacetId, resourceId, take), cancellationToken);
                foreach (var item in page.Items)
                {
                    if (!found.TryGetValue(item.ResourceId, out var entry))
                        found[item.ResourceId] = entry = (item.DefinitionId, []);
                    entry.Via.Add(field.Key);
                }
            }
        }

        return found
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new AsterReferrer(pair.Key, pair.Value.DefinitionId, pair.Value.Via))
            .ToList();
    }
}
