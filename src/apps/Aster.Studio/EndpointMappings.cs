using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Microsoft.AspNetCore.Mvc;

namespace Aster.Studio;

internal static class EndpointMappings
{
    private static readonly string[] FixedChannels = ["preview", "live"];
    private const string TaggableAspectKey = "taggable";
    private const string TagsFacetId = "tags";

    /// <summary>
    /// Maps Studio's view endpoints under <c>/api/studio</c>: aggregates the SPA's screens need and the
    /// shared-aspect library. Plain resource and definition operations come from <c>MapAster</c> (#110).
    /// </summary>
    public static RouteGroupBuilder MapStudioApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/studio");

        api.MapAspectApi();

        api.MapGet("/definitions", GetDefinitionsAsync)
            .WithName("GetDefinitionSummaries");

        api.MapGet("/resources", GetResourcesAsync)
            .WithName("GetResourceListItems");

        return api;
    }

    private static async Task<IResult> GetDefinitionsAsync(
        IResourceDefinitionStore definitionStore,
        IResourceQueryService queryService,
        CancellationToken cancellationToken)
    {
        var definitions = (await definitionStore.ListDefinitionsAsync(cancellationToken)).ToList();
        var summaries = new List<DefinitionSummaryDto>(definitions.Count);
        foreach (var definition in definitions)
        {
            var resources = await queryService.QueryAsync(new ResourceQuery { DefinitionId = definition.DefinitionId }, cancellationToken);
            summaries.Add(new DefinitionSummaryDto(
                definition.DefinitionId,
                definition.Version,
                definition.IsSingleton,
                DemoDefinitions.ToDisplayName(definition.DefinitionId),
                definition.AspectDefinitions.Count,
                resources.Count()));
        }

        return Results.Ok(summaries);
    }

    private static async Task<IResult> GetResourcesAsync(
        string? definitionId,
        int? skip,
        int? take,
        [FromQuery] string[]? tag,
        IResourceDefinitionStore definitionStore,
        IResourceQueryService queryService,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return Results.BadRequest(new ErrorEnvelope("definitionId is required."));

        if (await definitionStore.GetDefinitionAsync(definitionId, cancellationToken) is not { } definition)
            return Results.NotFound(new ErrorEnvelope($"Definition '{definitionId}' was not found."));

        // Tag filtering runs in the provider as ArrayContainsAny over the Taggable aspect's string[] facet.
        var tags = tag?.Select(static value => value.Trim()).Where(static value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        FilterExpression? filter = tags.Length == 0
            ? null
            : definition.AspectDefinitions.ContainsKey(TaggableAspectKey)
                ? new FacetValueFilter(TaggableAspectKey, TagsFacetId, tags, ComparisonOperator.ArrayContainsAny)
                : new AspectPresenceFilter(TaggableAspectKey);

        var normalizedSkip = Math.Max(0, skip ?? 0);
        var normalizedTake = Math.Clamp(take ?? 20, 1, 100);
        var all = (await queryService.QueryAsync(new ResourceQuery { DefinitionId = definitionId, Filter = filter }, cancellationToken)).ToList();
        var paged = (await queryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = definitionId,
            Filter = filter,
            Skip = normalizedSkip,
            Take = normalizedTake,
        }, cancellationToken)).ToList();

        var histories = await GetHistoriesAsync(historyService, paged.Select(static resource => resource.ResourceId).ToList(), cancellationToken);
        var items = paged.Select(resource => new ResourceListItemDto(
            resource.ResourceId,
            resource.DefinitionId,
            resource.Version,
            resource.Created,
            GetTitle(resource),
            GetActiveChannels(histories[resource.ResourceId]).ToList(),
            GetTags(resource)))
            .ToList();

        return Results.Ok(new ResourceListResponse(items, all.Count));
    }

    private static async Task<Dictionary<string, ResourceVersionHistoryResult>> GetHistoriesAsync(
        IResourceVersionHistoryService historyService,
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
            return new Dictionary<string, ResourceVersionHistoryResult>(StringComparer.Ordinal);

        var result = await historyService.GetHistoriesAsync(new ResourceVersionHistoryBatchRequest { ResourceIds = resourceIds }, cancellationToken);
        return result.Histories.ToDictionary(static history => history.ResourceId, StringComparer.Ordinal);
    }

    private static string GetTitle(Resource resource)
    {
        if (resource.Aspects.TryGetValue("content", out var content)
            && DemoJsonValues.ToFacetDictionary(content).TryGetValue("title", out var title)
            && title is not null)
        {
            return title.ToString() ?? resource.ResourceId;
        }

        return resource.ResourceId;
    }

    private static IReadOnlyList<string> GetTags(Resource resource) =>
        resource.Aspects.TryGetValue(TaggableAspectKey, out var taggable)
        && DemoJsonValues.ToFacetDictionary(taggable).TryGetValue(TagsFacetId, out var tags)
        && tags is IEnumerable<object?> values
            ? values.OfType<string>().ToList()
            : [];

    private static IEnumerable<string> GetActiveChannels(ResourceVersionHistoryResult history) =>
        OrderChannels(history.Versions.SelectMany(static version => version.ActiveChannels).Distinct(StringComparer.Ordinal));

    private static IEnumerable<string> OrderChannels(IEnumerable<string> channels) =>
        channels.OrderBy(static channel => ChannelOrder(channel)).ThenBy(static channel => channel, StringComparer.Ordinal);

    private static int ChannelOrder(string channel) => Array.IndexOf(FixedChannels, channel) is var index && index >= 0 ? index : FixedChannels.Length;
}
