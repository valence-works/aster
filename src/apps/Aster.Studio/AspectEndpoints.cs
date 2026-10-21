using System.Text.RegularExpressions;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Studio;

/// <summary>
/// The Aspect Library API over <see cref="IAspectDefinitionStore"/> and <see cref="IAspectUpgradeService"/>.
/// </summary>
internal static partial class AspectEndpoints
{
    // The store appends unconditionally, so "create only if missing" and "save only if still on the base
    // version" are check-then-register sequences. Studio runs as one process; serializing them here is enough.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static RouteGroupBuilder MapAspectApi(this RouteGroupBuilder api)
    {
        api.MapGet("/aspects", GetAspectsAsync)
            .WithName("GetAspects");

        api.MapGet("/aspects/{aspectId}", GetAspectAsync)
            .WithName("GetAspect");

        api.MapPost("/aspects", CreateAspectAsync)
            .WithName("CreateAspect");

        api.MapPut("/aspects/{aspectId}", UpdateAspectAsync)
            .WithName("UpdateAspect");

        api.MapGet("/aspects/{aspectId}/upgrade-preview", PreviewUpgradeAsync)
            .WithName("PreviewAspectUpgrade");

        api.MapPost("/aspects/{aspectId}/upgrade", ApplyUpgradeAsync)
            .WithName("ApplyAspectUpgrade");

        return api;
    }

    private static async Task<IResult> GetAspectsAsync(
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        IResourceDefinitionStore definitionStore,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        var usages = await GetUsagesAsync(definitionStore, tenant, cancellationToken);
        var aspects = await aspectStore.ListAsync(tenant, cancellationToken);
        var result = new List<SharedAspectDto>(aspects.Count);
        foreach (var aspect in aspects)
            result.Add(await ToDtoAsync(aspectStore, tenant, aspect, usages, cancellationToken));

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAspectAsync(
        string aspectId,
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        IResourceDefinitionStore definitionStore,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        var aspect = await aspectStore.GetLatestAsync(aspectId, tenant, cancellationToken);
        if (aspect is null)
            return Results.NotFound(new ErrorEnvelope($"Aspect '{aspectId}' was not found."));

        var usages = await GetUsagesAsync(definitionStore, tenant, cancellationToken);
        return Results.Ok(await ToDtoAsync(aspectStore, tenant, aspect, usages, cancellationToken));
    }

    private static async Task<IResult> CreateAspectAsync(
        CreateSharedAspectRequest request,
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        if (string.IsNullOrWhiteSpace(request.AspectId) || !AspectIdPattern().IsMatch(request.AspectId))
            return BadAspect("aspect-id-invalid", request.AspectId, "Aspect id must start with a letter and contain only letters, digits, '-' or '_'.");

        if (ValidateFacets(request.AspectId, request.Facets) is { } invalid)
            return invalid;

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            if (await aspectStore.GetLatestAsync(request.AspectId, tenant, cancellationToken) is { } existing)
                return Results.Conflict(new ErrorEnvelope($"Aspect '{request.AspectId}' already exists (version {existing.Version})."));

            var registered = await aspectStore.RegisterAsync(
                DemoDefinitions.BuildAspect(request.AspectId, request.RequiresName, request.Facets),
                tenant,
                cancellationToken);

            return Results.Created(
                $"/api/studio/aspects/{registered.AspectDefinitionId}",
                DemoDefinitions.ToSharedAspectDto(registered, [registered.Version], []));
        }
        catch (FacetMetadataValidationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope(exception.Diagnostics.Select(ToDiagnosticDto).ToList()));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static async Task<IResult> UpdateAspectAsync(
        string aspectId,
        UpdateSharedAspectRequest request,
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        IResourceDefinitionStore definitionStore,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        if (ValidateFacets(aspectId, request.Facets) is { } invalid)
            return invalid;

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var latest = await aspectStore.GetLatestAsync(aspectId, tenant, cancellationToken);
            if (latest is null)
                return Results.NotFound(new ErrorEnvelope($"Aspect '{aspectId}' was not found."));

            if (latest.Version != request.BaseVersion)
            {
                return Results.Conflict(new ErrorEnvelope(
                    $"Aspect '{aspectId}' is at version {latest.Version}, not {request.BaseVersion}. Reload and reapply your changes."));
            }

            var candidate = DemoDefinitions.BuildAspect(aspectId, request.RequiresName, request.Facets);

            // Versions are append-only; saving unchanged facets would add a version that differs only in number.
            var saved = DemoDefinitions.HasSameShape(candidate, latest)
                ? latest
                : await aspectStore.RegisterAsync(candidate, tenant, cancellationToken);

            var usages = await GetUsagesAsync(definitionStore, tenant, cancellationToken);
            return Results.Ok(await ToDtoAsync(aspectStore, tenant, saved, usages, cancellationToken));
        }
        catch (FacetMetadataValidationException exception)
        {
            return Results.BadRequest(new DiagnosticsEnvelope(exception.Diagnostics.Select(ToDiagnosticDto).ToList()));
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static async Task<IResult> PreviewUpgradeAsync(
        string aspectId,
        int? targetVersion,
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        IAspectUpgradeService upgradeService,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        if (await aspectStore.GetLatestAsync(aspectId, tenant, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Aspect '{aspectId}' was not found."));

        var preview = await upgradeService.PreviewAsync(aspectId, targetVersion, tenant, cancellationToken);
        return Results.Ok(new AspectUpgradePreviewDto(
            preview.AspectDefinitionId,
            preview.TargetVersion,
            preview.Candidates.Select(static candidate => new AspectUpgradeCandidateDto(
                candidate.DefinitionId,
                candidate.DefinitionVersion,
                candidate.AttachmentKey,
                candidate.PinnedVersion,
                candidate.Changes.Select(static change => new AspectFacetChangeDto(
                    change.FacetDefinitionId,
                    ToKebab(change.Kind),
                    change.From,
                    change.To)).ToList())).ToList(),
            preview.Diagnostics.Select(ToDiagnosticDto).ToList()));
    }

    private static async Task<IResult> ApplyUpgradeAsync(
        string aspectId,
        AspectUpgradeRequestDto request,
        HttpContext http,
        IAspectDefinitionStore aspectStore,
        IAspectUpgradeService upgradeService,
        CancellationToken cancellationToken)
    {
        var tenant = http.GetTenantScope();
        if (await aspectStore.GetLatestAsync(aspectId, tenant, cancellationToken) is null)
            return Results.NotFound(new ErrorEnvelope($"Aspect '{aspectId}' was not found."));

        if (request.DefinitionIds is not { Count: > 0 })
            return Results.BadRequest(new ErrorEnvelope("Select at least one content type to upgrade."));

        var result = await upgradeService.ApplyAsync(new AspectUpgradeRequest
        {
            TenantScope = tenant,
            AspectDefinitionId = aspectId,
            TargetVersion = request.TargetVersion,
            DefinitionIds = request.DefinitionIds,
        }, cancellationToken);

        return Results.Ok(new AspectUpgradeResultDto(
            result.TargetVersion,
            result.Outcomes.Select(static outcome => new AspectUpgradeOutcomeDto(
                outcome.DefinitionId,
                ToKebab(outcome.Status),
                outcome.NewDefinitionVersion,
                outcome.AttachmentKeys,
                outcome.Diagnostics.Select(ToDiagnosticDto).ToList())).ToList(),
            result.Diagnostics.Select(ToDiagnosticDto).ToList()));
    }

    private static async Task<SharedAspectDto> ToDtoAsync(
        IAspectDefinitionStore aspectStore,
        TenantScope tenant,
        AspectDefinition aspect,
        ILookup<string, AspectUsageDto> usages,
        CancellationToken cancellationToken)
    {
        var versions = await aspectStore.ListVersionsAsync(aspect.AspectDefinitionId, tenant, cancellationToken);
        return DemoDefinitions.ToSharedAspectDto(
            aspect,
            versions.Select(static version => version.Version).ToList(),
            usages[aspect.AspectDefinitionId].ToList());
    }

    // Usage is read from each content type's latest version, which is what upgrades re-pin.
    private static async Task<ILookup<string, AspectUsageDto>> GetUsagesAsync(
        IResourceDefinitionStore definitionStore,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        var definitions = await definitionStore.ListDefinitionsAsync(tenant, cancellationToken);
        return definitions
            .SelectMany(static definition => definition.AspectDefinitions
                .Where(static attachment => attachment.Value.IsShared)
                .Select(attachment => new
                {
                    attachment.Value.AspectDefinitionId,
                    Usage = new AspectUsageDto(definition.DefinitionId, definition.Version, attachment.Key, attachment.Value.Version),
                }))
            .OrderBy(static item => item.Usage.DefinitionId, StringComparer.Ordinal)
            .ThenBy(static item => item.Usage.AttachmentKey, StringComparer.Ordinal)
            .ToLookup(static item => item.AspectDefinitionId, static item => item.Usage, StringComparer.Ordinal);
    }

    private static IResult? ValidateFacets(string aspectId, IReadOnlyList<FacetDto>? facets)
    {
        if (facets is not { Count: > 0 })
            return BadAspect("aspect-facets-required", aspectId, "An aspect must declare at least one facet.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var facet in facets)
        {
            if (string.IsNullOrWhiteSpace(facet.FacetId))
                return BadAspect("aspect-facet-id-required", aspectId, "Every facet needs an id.");

            if (!seen.Add(facet.FacetId))
                return Results.BadRequest(new DiagnosticsEnvelope([new DiagnosticDto("aspect-facet-id-duplicate", aspectId, facet.FacetId, $"Facet '{facet.FacetId}' is declared more than once.")]));
        }

        return null;
    }

    private static IResult BadAspect(string code, string? aspectId, string message) =>
        Results.BadRequest(new DiagnosticsEnvelope([new DiagnosticDto(code, aspectId, null, message)]));

    private static DiagnosticDto ToDiagnosticDto(FacetMetadataDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.AspectKey,
        diagnostic.FacetDefinitionId,
        diagnostic.Message);

    private static DiagnosticDto ToDiagnosticDto(SharedAspectDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.AttachmentKey,
        null,
        diagnostic.Message);

    private static string ToKebab<TEnum>(TEnum value) where TEnum : struct, Enum =>
        KebabBoundary().Replace(value.ToString(), "-$1").ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex AspectIdPattern();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex KebabBoundary();
}
