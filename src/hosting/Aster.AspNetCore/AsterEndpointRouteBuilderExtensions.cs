using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.AspNetCore;

/// <summary>The body of <c>POST /resources</c>.</summary>
/// <param name="DefinitionId">The definition of the new resource.</param>
/// <param name="ResourceId">The resource ID; generated when omitted.</param>
/// <param name="Aspects">The aspect payloads, by attachment key.</param>
public sealed record AsterCreateResourceRequest(string DefinitionId, string? ResourceId, IReadOnlyDictionary<string, JsonElement>? Aspects);

/// <summary>The body of <c>PUT /resources/{resourceId}</c>.</summary>
/// <param name="BaseVersion">The latest version the edit is based on; a newer latest version returns <c>409</c>.</param>
/// <param name="Aspects">The aspect payloads to replace, by attachment key.</param>
public sealed record AsterUpdateResourceRequest(int BaseVersion, IReadOnlyDictionary<string, JsonElement>? Aspects);

/// <summary>The body of <c>POST /resources/{resourceId}/activate</c> and <c>/deactivate</c>.</summary>
/// <param name="Version">The resource version.</param>
/// <param name="Channel">The activation channel, such as <c>Published</c>.</param>
/// <param name="AllowMultipleActive">When activating, keep other versions active in the channel.</param>
public sealed record AsterActivationRequest(int Version, string Channel, bool AllowMultipleActive = false);

/// <summary>A page of resources from <c>GET /resources</c> or <c>POST /resources/query</c>.</summary>
/// <param name="Items">The resources the caller may read.</param>
/// <param name="HasMore">Whether the provider has more results after this page.</param>
public sealed record AsterResourcePage(IReadOnlyList<Resource> Items, bool HasMore);

/// <summary>
/// Maps Aster's REST surface onto a host's endpoint routing (#108).
/// </summary>
public static class AsterEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Aster endpoints under <paramref name="prefix"/> and returns their route group, so the host can add
    /// conventions to all of them, for example <c>app.MapAster("/api/aster").RequireAuthorization("aster")</c>.
    /// Requires <c>AddAsterCore</c> (and a persistence provider) in the host's services. The tenant comes from a
    /// registered <see cref="ITenantScopeAccessor"/>, and resource-level checks from a registered
    /// <see cref="IAsterResourceAuthorizer"/>; without them, every request uses the default tenant and is allowed.
    /// </summary>
    /// <param name="endpoints">The host's endpoint route builder.</param>
    /// <param name="prefix">The route prefix. Defaults to <c>/api/aster</c>.</param>
    /// <param name="configure">Configures groups, read-only mode, per-group policies, paging and JSON options.</param>
    /// <returns>The route group holding every Aster endpoint.</returns>
    public static RouteGroupBuilder MapAster(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/aster",
        Action<AsterEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(prefix);

        var options = new AsterEndpointOptions();
        configure?.Invoke(options);
        if (options.DefaultPageSize < 1 || options.MaxPageSize < options.DefaultPageSize)
            throw new ArgumentException("Page sizes must satisfy 1 <= DefaultPageSize <= MaxPageSize.", nameof(configure));

        // Every body read and response of these endpoints uses these options, not the host's (#190).
        var serializerOptions = AsterJson.MakeReadOnly(options.SerializerOptions);

        var root = endpoints.MapGroup(prefix);
        root.WithMetadata(new AsterJsonMetadata(serializerOptions));
        root.AddEndpointFilter<AsterExceptionFilter>();

        if (options.Groups.HasFlag(AsterEndpointGroups.Definitions))
            MapDefinitions(Group(root, "/definitions", "Aster definitions", AsterEndpointGroups.Definitions, options), options);
        if (options.Groups.HasFlag(AsterEndpointGroups.Aspects))
            MapAspects(Group(root, "/aspects", "Aster aspects", AsterEndpointGroups.Aspects, options), options);
        if (options.Groups.HasFlag(AsterEndpointGroups.Resources))
            MapResources(Group(root, "/resources", "Aster resources", AsterEndpointGroups.Resources, options), options);

        return root;
    }

    private static RouteGroupBuilder Group(RouteGroupBuilder root, string path, string tag, AsterEndpointGroups group, AsterEndpointOptions options)
    {
        var builder = root.MapGroup(path).WithTags(tag);
        if (options.AuthorizationPolicies.TryGetValue(group, out var policy))
            builder.RequireAuthorization(policy);
        options.ConfigureGroup?.Invoke(group, builder);
        return builder;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Definitions
    // ──────────────────────────────────────────────────────────────────────────

    private static void MapDefinitions(RouteGroupBuilder group, AsterEndpointOptions options)
    {
        group.MapGet("", async (HttpContext http, IResourceDefinitionStore store, CancellationToken ct) =>
                AsterJson.Ok((await store.ListDefinitionsAsync(Tenant(http), ct))
                    .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal)
                    .ToList()))
            .WithSummary("List the latest version of each resource definition.")
            .Produces<IReadOnlyList<ResourceDefinition>>();

        group.MapGet("/{definitionId}", async (string definitionId, int? version, HttpContext http, IResourceDefinitionStore store, CancellationToken ct) =>
                await FindDefinitionAsync(store, definitionId, version, Tenant(http), ct) is { } definition
                    ? AsterJson.Ok(definition)
                    : DefinitionNotFound(definitionId, version))
            .WithSummary("Get a resource definition: the latest version, or the one named by ?version.")
            .Produces<ResourceDefinition>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{definitionId}/schema", async (string definitionId, int? version, HttpContext http, IResourceDefinitionStore store, IResourceSchemaExporter exporter, CancellationToken ct) =>
                await FindDefinitionAsync(store, definitionId, version, Tenant(http), ct) is { } definition
                    ? AsterJson.Schema(exporter.Export(definition))
                    : DefinitionNotFound(definitionId, version))
            .WithSummary("Export a resource definition as a JSON Schema 2020-12 document.")
            .Produces<object>(contentType: "application/schema+json")
            .ProducesProblem(StatusCodes.Status404NotFound);

        if (options.ReadOnly)
            return;

        group.MapPost("", RegisterDefinitionAsync)
            .WithSummary("Register a new version of a resource definition.")
            .WithDescription("The store assigns the version. With ?expectedLatestVersion, registration happens only when the latest version is still that one (0: the definition must not exist), and returns 409 otherwise. Shared aspect attachments are re-resolved from the aspect store by (id, version).")
            .Accepts<ResourceDefinition>("application/json")
            .Produces<ResourceDefinition>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> RegisterDefinitionAsync(
        int? expectedLatestVersion,
        HttpContext http,
        IResourceDefinitionStore store,
        IAspectDefinitionStore aspects,
        CancellationToken ct)
    {
        var (definition, bodyProblem) = await AsterJson.ReadBodyAsync<ResourceDefinition>(http);
        if (definition is null)
            return bodyProblem!;
        if (string.IsNullOrWhiteSpace(definition.DefinitionId))
            return AsterProblems.Validation("definition-id-required", "definitionId is required.");
        if (expectedLatestVersion is < 0)
            return AsterProblems.Validation("expected-version-invalid", "expectedLatestVersion must not be negative.");

        var tenant = Tenant(http);

        // A shared attachment is a pin: embed the stored version rather than whatever copy the client sent (ADR 0003).
        var resolved = new Dictionary<string, AspectDefinition>(StringComparer.Ordinal);
        var missing = new List<AsterDiagnostic>();
        foreach (var (key, aspect) in definition.AspectDefinitions)
        {
            if (!aspect.IsShared)
            {
                resolved[key] = aspect;
                continue;
            }

            if (await aspects.GetVersionAsync(aspect.AspectDefinitionId, aspect.Version, tenant, ct) is { } stored)
                resolved[key] = stored with { IsShared = true };
            else
                missing.Add(new AsterDiagnostic("aspect-definition-not-found", key, null, $"Aspect definition '{aspect.AspectDefinitionId}' version {aspect.Version} does not exist."));
        }

        if (missing.Count > 0)
            return AsterProblems.Validation("Shared aspects could not be resolved.", missing);

        var candidate = definition with { TenantScope = tenant, AspectDefinitions = resolved };
        ResourceDefinition? registered;
        if (expectedLatestVersion is { } expected)
        {
            var result = await store.RegisterDefinitionIfLatestAsync(candidate, expected, tenant, ct);
            if (!result.IsRegistered)
            {
                return AsterProblems.Conflict(
                    "definition-version-conflict",
                    $"Definition '{definition.DefinitionId}' is at version {result.LatestVersion}, not {expected}.");
            }

            registered = result.Definition;
        }
        else
        {
            await store.RegisterDefinitionAsync(candidate, tenant, ct);
            registered = await store.GetDefinitionAsync(definition.DefinitionId, tenant, ct);
        }

        return AsterJson.Created($"{http.Request.PathBase}{http.Request.Path.Value?.TrimEnd('/')}/{Uri.EscapeDataString(definition.DefinitionId)}", registered);
    }

    private static async ValueTask<ResourceDefinition?> FindDefinitionAsync(
        IResourceDefinitionStore store, string definitionId, int? version, TenantScope tenant, CancellationToken ct) =>
        version is { } specific
            ? await store.GetDefinitionVersionAsync(definitionId, specific, tenant, ct)
            : await store.GetDefinitionAsync(definitionId, tenant, ct);

    private static IResult DefinitionNotFound(string definitionId, int? version) =>
        AsterProblems.NotFound(version is { } v
            ? $"Definition '{definitionId}' version {v} was not found."
            : $"Definition '{definitionId}' was not found.");

    // ──────────────────────────────────────────────────────────────────────────
    // Shared aspects
    // ──────────────────────────────────────────────────────────────────────────

    private static void MapAspects(RouteGroupBuilder group, AsterEndpointOptions options)
    {
        group.MapGet("", async (HttpContext http, IAspectDefinitionStore store, CancellationToken ct) =>
                AsterJson.Ok(await store.ListAsync(Tenant(http), ct)))
            .WithSummary("List the latest version of each shared aspect definition.")
            .Produces<IReadOnlyList<AspectDefinition>>();

        group.MapGet("/{aspectId}", async (string aspectId, int? version, HttpContext http, IAspectDefinitionStore store, CancellationToken ct) =>
                await FindAspectAsync(store, aspectId, version, Tenant(http), ct) is { } aspect
                    ? AsterJson.Ok(aspect)
                    : AspectNotFound(aspectId, version))
            .WithSummary("Get a shared aspect definition: the latest version, or the one named by ?version.")
            .Produces<AspectDefinition>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{aspectId}/versions", async (string aspectId, HttpContext http, IAspectDefinitionStore store, CancellationToken ct) =>
                await store.ListVersionsAsync(aspectId, Tenant(http), ct) is { Count: > 0 } versions
                    ? AsterJson.Ok(versions)
                    : AspectNotFound(aspectId, null))
            .WithSummary("List every version of a shared aspect definition.")
            .Produces<IReadOnlyList<AspectDefinition>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{aspectId}/schema", async (string aspectId, int? version, HttpContext http, IAspectDefinitionStore store, IResourceSchemaExporter exporter, CancellationToken ct) =>
                await FindAspectAsync(store, aspectId, version, Tenant(http), ct) is { } aspect
                    ? AsterJson.Schema(exporter.ExportAspect(aspect))
                    : AspectNotFound(aspectId, version))
            .WithSummary("Export a shared aspect definition's payload as a JSON Schema 2020-12 document.")
            .Produces<object>(contentType: "application/schema+json")
            .ProducesProblem(StatusCodes.Status404NotFound);

        if (options.ReadOnly)
            return;

        group.MapPost("", async (int? expectedLatestVersion, HttpContext http, IAspectDefinitionStore store, CancellationToken ct) =>
            {
                var (aspect, bodyProblem) = await AsterJson.ReadBodyAsync<AspectDefinition>(http);
                if (aspect is null)
                    return bodyProblem!;
                if (string.IsNullOrWhiteSpace(aspect.AspectDefinitionId))
                    return AsterProblems.Validation("aspect-id-required", "aspectDefinitionId is required.");
                if (expectedLatestVersion is < 0)
                    return AsterProblems.Validation("expected-version-invalid", "expectedLatestVersion must not be negative.");

                AspectDefinition registered;
                if (expectedLatestVersion is { } expected)
                {
                    var result = await store.RegisterIfLatestAsync(aspect, expected, Tenant(http), ct);
                    if (!result.IsRegistered)
                    {
                        return AsterProblems.Conflict(
                            "aspect-version-conflict",
                            $"Aspect definition '{aspect.AspectDefinitionId}' is at version {result.LatestVersion}, not {expected}.");
                    }

                    registered = result.Aspect!;
                }
                else
                {
                    registered = await store.RegisterAsync(aspect, Tenant(http), ct);
                }

                return AsterJson.Created($"{http.Request.PathBase}{http.Request.Path.Value?.TrimEnd('/')}/{Uri.EscapeDataString(registered.AspectDefinitionId)}", registered);
            })
            .WithSummary("Register a new version of a shared aspect definition; the store assigns the version.")
            .WithDescription("With ?expectedLatestVersion, registration happens only when the latest version is still that one (0: the aspect must not exist), and returns 409 otherwise.")
            .Accepts<AspectDefinition>("application/json")
            .Produces<AspectDefinition>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async ValueTask<AspectDefinition?> FindAspectAsync(
        IAspectDefinitionStore store, string aspectId, int? version, TenantScope tenant, CancellationToken ct) =>
        version is { } specific
            ? await store.GetVersionAsync(aspectId, specific, tenant, ct)
            : await store.GetLatestAsync(aspectId, tenant, ct);

    private static IResult AspectNotFound(string aspectId, int? version) =>
        AsterProblems.NotFound(version is { } v
            ? $"Aspect definition '{aspectId}' version {v} was not found."
            : $"Aspect definition '{aspectId}' was not found.");

    // ──────────────────────────────────────────────────────────────────────────
    // Resources
    // ──────────────────────────────────────────────────────────────────────────

    private static void MapResources(RouteGroupBuilder group, AsterEndpointOptions options)
    {
        group.MapGet("", (string? definitionId, string? scope, string? channel, string? lifecycle, int? skip, int? take, HttpContext http, IResourceQueryService queries, CancellationToken ct) =>
                QueryAsync(new AsterQueryRequest
                {
                    DefinitionId = definitionId,
                    Scope = scope,
                    Channel = channel,
                    Lifecycle = lifecycle,
                    Skip = skip,
                    Take = take,
                }, http, queries, options, ct))
            .WithSummary("List resources. Archived and soft-deleted resources are excluded unless ?lifecycle=all, archived or softDeleted.")
            .Produces<AsterResourcePage>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/query", async (HttpContext http, IResourceQueryService queries, CancellationToken ct) =>
            {
                var (request, bodyProblem) = await AsterJson.ReadBodyAsync<AsterQueryRequest>(http);
                return request is null ? bodyProblem! : await QueryAsync(request, http, queries, options, ct);
            })
            .WithSummary("Query resources with the portable query model (filters, sorting, paging).")
            .Accepts<AsterQueryRequest>("application/json")
            .Produces<AsterResourcePage>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapGet("/{resourceId}", async (string resourceId, int? version, HttpContext http, IResourceManager manager, CancellationToken ct) =>
            {
                var tenant = Tenant(http);
                var resource = version is { } specific
                    ? await manager.GetVersionAsync(resourceId, specific, tenant, ct)
                    : await manager.GetLatestVersionAsync(resourceId, tenant, ct);
                if (resource is null)
                    return ResourceNotFound(resourceId, version);

                return await AuthorizeAsync(http, AsterResourceOperation.Read, resource.DefinitionId, resourceId, resource, ct)
                    ? AsterJson.Ok(resource)
                    : AsterProblems.Forbidden();
            })
            .WithSummary("Get a resource: the latest version, or the one named by ?version.")
            .Produces<Resource>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{resourceId}/versions", async (string resourceId, HttpContext http, IResourceManager manager, IResourceVersionHistoryService history, CancellationToken ct) =>
            {
                var tenant = Tenant(http);
                if (await manager.GetLatestVersionAsync(resourceId, tenant, ct) is not { } latest)
                    return ResourceNotFound(resourceId, null);
                if (!await AuthorizeAsync(http, AsterResourceOperation.Read, latest.DefinitionId, resourceId, latest, ct))
                    return AsterProblems.Forbidden();

                return AsterJson.Ok(await history.GetHistoryAsync(new ResourceVersionHistoryRequest { TenantScope = tenant, ResourceId = resourceId }, ct));
            })
            .WithSummary("Get a resource's version history: every version with its active channels and lifecycle state.")
            .Produces<ResourceVersionHistoryResult>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        if (options.ReadOnly)
            return;

        group.MapPost("", async (HttpContext http, IResourceDefinitionStore definitions, IResourceManager manager, CancellationToken ct) =>
            {
                var (request, bodyProblem) = await AsterJson.ReadBodyAsync<AsterCreateResourceRequest>(http);
                if (request is null)
                    return bodyProblem!;

                var tenant = Tenant(http);
                if (string.IsNullOrWhiteSpace(request.DefinitionId))
                    return AsterProblems.Validation("definition-id-required", "definitionId is required.");
                if (await definitions.GetDefinitionAsync(request.DefinitionId, tenant, ct) is null)
                    return DefinitionNotFound(request.DefinitionId, null);
                if (!await AuthorizeAsync(http, AsterResourceOperation.Create, request.DefinitionId, request.ResourceId, null, ct))
                    return AsterProblems.Forbidden();

                var created = await manager.CreateAsync(request.DefinitionId, new CreateResourceRequest
                {
                    TenantScope = tenant,
                    ResourceId = request.ResourceId,
                    InitialAspects = ToAspects(request.Aspects),
                }, ct);
                return AsterJson.Created($"{http.Request.PathBase}{http.Request.Path.Value?.TrimEnd('/')}/{Uri.EscapeDataString(created.ResourceId)}", created);
            })
            .WithSummary("Create version 1 of a resource.")
            .Accepts<AsterCreateResourceRequest>("application/json")
            .Produces<Resource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPut("/{resourceId}", async (string resourceId, HttpContext http, IResourceManager manager, CancellationToken ct) =>
            {
                var (request, bodyProblem) = await AsterJson.ReadBodyAsync<AsterUpdateResourceRequest>(http);
                if (request is null)
                    return bodyProblem!;

                var tenant = Tenant(http);
                if (await manager.GetLatestVersionAsync(resourceId, tenant, ct) is not { } latest)
                    return ResourceNotFound(resourceId, null);
                if (!await AuthorizeAsync(http, AsterResourceOperation.Update, latest.DefinitionId, resourceId, latest, ct))
                    return AsterProblems.Forbidden();

                var updated = await manager.UpdateAsync(resourceId, new UpdateResourceRequest
                {
                    TenantScope = tenant,
                    BaseVersion = request.BaseVersion,
                    AspectUpdates = ToAspects(request.Aspects),
                }, ct);
                return AsterJson.Ok(updated);
            })
            .WithSummary("Save a new version of a resource, based on baseVersion.")
            .Accepts<AsterUpdateResourceRequest>("application/json")
            .Produces<Resource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/{resourceId}/activate", (string resourceId, HttpContext http, IResourceManager manager, CancellationToken ct) =>
                ChangeActivationAsync(resourceId, activate: true, http, manager, ct))
            .WithSummary("Activate a resource version in a channel.")
            .Accepts<AsterActivationRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/{resourceId}/deactivate", (string resourceId, HttpContext http, IResourceManager manager, CancellationToken ct) =>
                ChangeActivationAsync(resourceId, activate: false, http, manager, ct))
            .WithSummary("Deactivate a resource version in a channel.")
            .Accepts<AsterActivationRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> QueryAsync(
        AsterQueryRequest request,
        HttpContext http,
        IResourceQueryService queries,
        AsterEndpointOptions options,
        CancellationToken ct)
    {
        if (!AsterQueryTranslator.TryTranslate(request, Tenant(http), options.DefaultPageSize, options.MaxPageSize, out var query, out var error))
            return AsterProblems.Validation(error!.Message, [error]);

        // One extra row tells whether another page exists.
        var take = query.Take!.Value;
        var rows = (await queries.QueryAsync(query with { Take = take + 1 }, ct)).ToList();
        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var items = new List<Resource>(rows.Count);
        foreach (var resource in rows)
        {
            if (await AuthorizeAsync(http, AsterResourceOperation.Read, resource.DefinitionId, resource.ResourceId, resource, ct))
                items.Add(resource);
        }

        return AsterJson.Ok(new AsterResourcePage(items, hasMore));
    }

    private static async Task<IResult> ChangeActivationAsync(
        string resourceId,
        bool activate,
        HttpContext http,
        IResourceManager manager,
        CancellationToken ct)
    {
        var (request, bodyProblem) = await AsterJson.ReadBodyAsync<AsterActivationRequest>(http);
        if (request is null)
            return bodyProblem!;
        if (string.IsNullOrWhiteSpace(request.Channel))
            return AsterProblems.Validation("channel-required", "channel is required.");

        var tenant = Tenant(http);
        if (await manager.GetVersionAsync(resourceId, request.Version, tenant, ct) is not { } version)
            return ResourceNotFound(resourceId, request.Version);
        if (!await AuthorizeAsync(http, AsterResourceOperation.Activate, version.DefinitionId, resourceId, version, ct))
            return AsterProblems.Forbidden();

        if (activate)
            await manager.ActivateAsync(resourceId, request.Version, request.Channel, tenant, request.AllowMultipleActive, ct);
        else
            await manager.DeactivateAsync(resourceId, request.Version, request.Channel, tenant, ct);

        return Results.NoContent();
    }

    private static Dictionary<string, object> ToAspects(IReadOnlyDictionary<string, JsonElement>? aspects) =>
        aspects?.ToDictionary(static pair => pair.Key, static pair => (object)pair.Value.Clone(), StringComparer.Ordinal) ?? [];

    private static IResult ResourceNotFound(string resourceId, int? version) =>
        AsterProblems.NotFound(version is { } v
            ? $"Version {v} of resource '{resourceId}' was not found."
            : $"Resource '{resourceId}' was not found.");

    private static TenantScope Tenant(HttpContext http) =>
        (http.RequestServices.GetService<ITenantScopeAccessor>() ?? DefaultTenantScopeAccessor.Instance).GetTenantScope(http);

    private static ValueTask<bool> AuthorizeAsync(
        HttpContext http, AsterResourceOperation operation, string definitionId, string? resourceId, Resource? resource, CancellationToken ct) =>
        (http.RequestServices.GetService<IAsterResourceAuthorizer>() ?? AllowAllResourceAuthorizer.Instance)
            .AuthorizeAsync(new AsterResourceAuthorizationContext(http, operation, definitionId, resourceId, resource), ct);
}
