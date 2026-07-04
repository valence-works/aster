using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.AdminDemo;

internal static class EndpointMappings
{
    private static readonly string[] FixedChannels = ["preview", "live"];

    public static RouteGroupBuilder MapAdminDemoApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/aspect-templates", () => AspectTemplateCatalog.All)
            .WithName("GetAspectTemplates");

        api.MapGet("/definitions", GetDefinitionsAsync)
            .WithName("GetDefinitions");

        api.MapGet("/definitions/{definitionId}", GetDefinitionAsync)
            .WithName("GetDefinition");

        api.MapPost("/definitions", CreateDefinitionAsync)
            .WithName("CreateDefinition");

        api.MapGet("/resources", GetResourcesAsync)
            .WithName("GetResources");

        api.MapGet("/resources/{resourceId}", GetResourceAsync)
            .WithName("GetResource");

        api.MapGet("/resources/{resourceId}/versions", GetResourceVersionsAsync)
            .WithName("GetResourceVersions");

        api.MapGet("/resources/{resourceId}/versions/{version:int}", GetResourceVersionAsync)
            .WithName("GetResourceVersion");

        api.MapPost("/resources", CreateResourceAsync)
            .WithName("CreateResource");

        api.MapPut("/resources/{resourceId}", UpdateResourceAsync)
            .WithName("UpdateResource");

        api.MapPost("/resources/{resourceId}/activate", ActivateResourceAsync)
            .WithName("ActivateResource");

        api.MapPost("/resources/{resourceId}/deactivate", DeactivateResourceAsync)
            .WithName("DeactivateResource");

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

    private static async Task<IResult> GetDefinitionAsync(
        string definitionId,
        IResourceDefinitionStore definitionStore,
        CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetDefinitionAsync(definitionId, cancellationToken);
        return definition is null
            ? Results.NotFound(new ErrorEnvelope($"Definition '{definitionId}' was not found."))
            : Results.Ok(DemoDefinitions.ToDetailDto(definition));
    }

    private static async Task<IResult> CreateDefinitionAsync(
        CreateDefinitionRequest request,
        IResourceDefinitionStore definitionStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefinitionId))
            return Results.BadRequest(new ErrorEnvelope("definitionId is required."));

        try
        {
            var aspects = new List<(string AspectKey, IReadOnlyList<FacetDto> Facets)>();
            foreach (var aspect in request.Aspects)
            {
                if (string.IsNullOrWhiteSpace(aspect.AspectKey))
                    return BadMetadata("facet-metadata-aspect-key-required", null, null, "Aspect key is required.");

                if (!string.IsNullOrWhiteSpace(aspect.TemplateKey))
                {
                    var template = AspectTemplateCatalog.Find(aspect.TemplateKey);
                    if (template is null)
                        return BadMetadata("facet-metadata-template-not-found", aspect.AspectKey, null, $"Template '{aspect.TemplateKey}' was not found.");

                    aspects.Add((aspect.AspectKey, template.Facets));
                    continue;
                }

                if (aspect.Facets is null || aspect.Facets.Count == 0)
                    return BadMetadata("facet-metadata-facets-required", aspect.AspectKey, null, "Inline aspects must declare at least one facet.");

                aspects.Add((aspect.AspectKey, aspect.Facets));
            }

            var definition = DemoDefinitions.BuildDefinition(request.DefinitionId, request.IsSingleton, aspects);
            await definitionStore.RegisterDefinitionAsync(definition, cancellationToken);
            var saved = await definitionStore.GetDefinitionAsync(request.DefinitionId, cancellationToken) ?? definition;
            return Results.Created($"/api/definitions/{request.DefinitionId}", DemoDefinitions.ToDetailDto(saved));
        }
        catch (FacetMetadataValidationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope(exception.Diagnostics.Select(ToDiagnosticDto).ToList()));
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope([new DiagnosticDto("facet-metadata-invalid-definition", null, null, exception.Message)]));
        }
    }

    private static async Task<IResult> GetResourcesAsync(
        string? definitionId,
        int? skip,
        int? take,
        IResourceDefinitionStore definitionStore,
        IResourceQueryService queryService,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return Results.BadRequest(new ErrorEnvelope("definitionId is required."));

        if (await definitionStore.GetDefinitionAsync(definitionId, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Definition '{definitionId}' was not found."));

        var normalizedSkip = Math.Max(0, skip ?? 0);
        var normalizedTake = Math.Clamp(take ?? 20, 1, 100);
        var all = (await queryService.QueryAsync(new ResourceQuery { DefinitionId = definitionId }, cancellationToken)).ToList();
        var paged = (await queryService.QueryAsync(new ResourceQuery
        {
            DefinitionId = definitionId,
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
            GetActiveChannels(histories[resource.ResourceId]).ToList()))
            .ToList();

        return Results.Ok(new ResourceListResponse(items, all.Count));
    }

    private static async Task<IResult> GetResourceAsync(
        string resourceId,
        IResourceManager resourceManager,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        var resource = await resourceManager.GetLatestVersionAsync(resourceId, cancellationToken);
        if (resource is null)
            return Results.NotFound(new ErrorEnvelope($"Resource '{resourceId}' was not found."));

        var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resourceId }, cancellationToken);
        return Results.Ok(ToDetailDto(resource, history));
    }

    private static async Task<IResult> GetResourceVersionsAsync(
        string resourceId,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resourceId }, cancellationToken);
        if (history.Versions.Count == 0)
            return Results.NotFound(new ErrorEnvelope($"Resource '{resourceId}' was not found."));

        return Results.Ok(history.Versions.Select(static version => new ResourceVersionDto(
            version.Version,
            version.Created,
            version.IsLatest,
            OrderChannels(version.ActiveChannels).ToList())).ToList());
    }

    private static async Task<IResult> GetResourceVersionAsync(
        string resourceId,
        int version,
        IResourceManager resourceManager,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        var resource = await resourceManager.GetVersionAsync(resourceId, version, cancellationToken);
        if (resource is null)
            return Results.NotFound(new ErrorEnvelope($"Version {version} of resource '{resourceId}' was not found."));

        var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resourceId }, cancellationToken);
        return Results.Ok(ToDetailDto(resource, history));
    }

    private static async Task<IResult> CreateResourceAsync(
        CreateResourceRequestDto request,
        IResourceDefinitionStore definitionStore,
        IResourceManager resourceManager,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DefinitionId))
            return Results.BadRequest(new ErrorEnvelope("definitionId is required."));

        if (await definitionStore.GetDefinitionAsync(request.DefinitionId, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Definition '{request.DefinitionId}' was not found."));

        try
        {
            var resource = await resourceManager.CreateAsync(request.DefinitionId, new Aster.Core.Abstractions.CreateResourceRequest
            {
                InitialAspects = DemoJsonValues.ToSdkAspects(request.Aspects),
            }, cancellationToken);
            var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resource.ResourceId }, cancellationToken);
            return Results.Created($"/api/resources/{resource.ResourceId}", ToDetailDto(resource, history));
        }
        catch (FacetValueValidationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope(exception.Diagnostics.Select(ToDiagnosticDto).ToList()));
        }
        catch (SingletonViolationException exception)
        {
            return Results.Conflict(new ErrorEnvelope(exception.Message));
        }
    }

    private static async Task<IResult> UpdateResourceAsync(
        string resourceId,
        UpdateResourceRequestDto request,
        IResourceManager resourceManager,
        IResourceVersionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetLatestVersionAsync(resourceId, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Resource '{resourceId}' was not found."));

        try
        {
            var resource = await resourceManager.UpdateAsync(resourceId, new Aster.Core.Abstractions.UpdateResourceRequest
            {
                BaseVersion = request.BaseVersion,
                AspectUpdates = DemoJsonValues.ToSdkAspects(request.Aspects),
            }, cancellationToken);
            var history = await historyService.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resource.ResourceId }, cancellationToken);
            return Results.Ok(ToDetailDto(resource, history));
        }
        catch (FacetValueValidationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope(exception.Diagnostics.Select(ToDiagnosticDto).ToList()));
        }
        catch (ConcurrencyException exception)
        {
            return Results.Conflict(new ErrorEnvelope(exception.Message));
        }
        catch (VersionNotFoundException exception)
        {
            return Results.NotFound(new ErrorEnvelope(exception.Message));
        }
    }

    private static async Task<IResult> ActivateResourceAsync(
        string resourceId,
        ActivationRequestDto request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (!IsFixedChannel(request.Channel))
            return Results.BadRequest(new ErrorEnvelope("Channel must be 'preview' or 'live'."));

        try
        {
            await resourceManager.ActivateAsync(resourceId, request.Version, request.Channel, cancellationToken: cancellationToken);
            return Results.Ok(new StatusDto("activated"));
        }
        catch (VersionNotFoundException exception)
        {
            return Results.NotFound(new ErrorEnvelope(exception.Message));
        }
        catch (ConcurrencyException exception)
        {
            return Results.Conflict(new ErrorEnvelope(exception.Message));
        }
    }

    private static async Task<IResult> DeactivateResourceAsync(
        string resourceId,
        ActivationRequestDto request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (!IsFixedChannel(request.Channel))
            return Results.BadRequest(new ErrorEnvelope("Channel must be 'preview' or 'live'."));

        if (await resourceManager.GetVersionAsync(resourceId, request.Version, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Version {request.Version} of resource '{resourceId}' was not found."));

        await resourceManager.DeactivateAsync(resourceId, request.Version, request.Channel, cancellationToken);
        return Results.Ok(new StatusDto("deactivated"));
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

    private static ResourceDetailDto ToDetailDto(Resource resource, ResourceVersionHistoryResult history) => new(
        resource.ResourceId,
        resource.DefinitionId,
        resource.Version,
        resource.Created,
        resource.Aspects.ToDictionary(static item => item.Key, static item => DemoJsonValues.ToFacetDictionary(item.Value), StringComparer.Ordinal),
        history.Versions
            .SelectMany(static version => version.ActiveChannels.Select(channel => new { channel, version.Version }))
            .GroupBy(static item => item.channel, StringComparer.Ordinal)
            .OrderBy(static group => ChannelOrder(group.Key))
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<int>)group.Select(static item => item.Version).Order().ToList(),
                StringComparer.Ordinal));

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

    private static IEnumerable<string> GetActiveChannels(ResourceVersionHistoryResult history) =>
        OrderChannels(history.Versions.SelectMany(static version => version.ActiveChannels).Distinct(StringComparer.Ordinal));

    private static IEnumerable<string> OrderChannels(IEnumerable<string> channels) =>
        channels.OrderBy(static channel => ChannelOrder(channel)).ThenBy(static channel => channel, StringComparer.Ordinal);

    private static int ChannelOrder(string channel) => Array.IndexOf(FixedChannels, channel) is var index && index >= 0 ? index : FixedChannels.Length;

    private static bool IsFixedChannel(string channel) => FixedChannels.Contains(channel, StringComparer.Ordinal);

    private static IResult BadMetadata(string code, string? aspectKey, string? facetId, string message) =>
        Results.BadRequest(new DiagnosticsEnvelope([new DiagnosticDto(code, aspectKey, facetId, message)]));

    private static DiagnosticDto ToDiagnosticDto(FacetMetadataDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.AspectKey,
        diagnostic.FacetDefinitionId,
        diagnostic.Message);

    private static DiagnosticDto ToDiagnosticDto(FacetValueDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.AspectKey,
        diagnostic.FacetDefinitionId,
        diagnostic.Message);
}
