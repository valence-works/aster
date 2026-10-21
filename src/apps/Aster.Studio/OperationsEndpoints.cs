using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Studio;

/// <summary>
/// Operations over shipped SDK services (#111): lifecycle markers and restore, schema status and upgrade, and
/// policy preview, application and pruning. Results keep the SDK's stable diagnostic codes; requests that fail
/// as a whole answer with problem details carrying them under <c>diagnostics</c>.
/// </summary>
internal static class OperationsEndpoints
{
    public static RouteGroupBuilder MapOperationsApi(this RouteGroupBuilder api)
    {
        api.MapGet("/resources/{resourceId}/lifecycle", GetLifecycleAsync)
            .WithName("GetResourceLifecycle");

        api.MapPost("/resources/{resourceId}/lifecycle", MarkAsync)
            .WithName("MarkResource");

        api.MapPost("/resources/{resourceId}/restore-preview", PreviewRestoreAsync)
            .WithName("PreviewResourceRestore");

        api.MapPost("/resources/{resourceId}/restore", RestoreAsync)
            .WithName("RestoreResource");

        api.MapGet("/resources/{resourceId}/schema", GetSchemaStatusAsync)
            .WithName("GetResourceSchemaStatus");

        api.MapPost("/resources/{resourceId}/schema-upgrade", UpgradeSchemaAsync)
            .WithName("UpgradeResourceSchema");

        api.MapGet("/definitions/{definitionId}/policies", GetPoliciesAsync)
            .WithName("GetDefinitionPolicies");

        api.MapPost("/definitions/{definitionId}/policies/preview", PreviewPoliciesAsync)
            .WithName("PreviewDefinitionPolicies");

        api.MapPost("/policies/apply", ApplyPoliciesAsync)
            .WithName("ApplyPolicyOutcomes");

        api.MapPost("/policies/prune", PruneVersionsAsync)
            .WithName("PruneVersions");

        return api;
    }

    // The marker store is provider-facing, but it is the only read that returns a marker's reason and timestamp;
    // version history reports the state alone.
    private static async Task<IResult> GetLifecycleAsync(
        string resourceId,
        IResourceManager resourceManager,
        IResourceLifecycleMarkerStore markerStore,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetLatestVersionAsync(resourceId, cancellationToken) is null)
            return ResourceNotFound(resourceId);

        var marker = await markerStore.GetMarkerAsync(resourceId, TenantScope.Default, cancellationToken);
        return Results.Ok(new LifecycleStateDto(
            resourceId,
            marker?.State ?? ResourceLifecycleMarkerState.None,
            marker?.MarkedAt,
            marker?.Reason));
    }

    private static async Task<IResult> MarkAsync(
        string resourceId,
        LifecycleMarkerRequestDto request,
        IResourceLifecycleMarkerService markerService,
        CancellationToken cancellationToken)
    {
        var result = await markerService.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = resourceId,
            State = request.State,
            MarkedAt = DateTimeOffset.UtcNow,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        }, cancellationToken);

        if (result.Succeeded)
            return Results.Ok(result);

        var status = result.Diagnostics.Any(static d => d.Code == ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound)
            ? StatusCodes.Status404NotFound
            : result.Diagnostics.Any(static d => d.Code == ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict)
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        return Problem(status, "The lifecycle marker was not applied.", result.Diagnostics);
    }

    private static async Task<IResult> PreviewRestoreAsync(
        string resourceId,
        LifecycleRestoreRequestDto request,
        IResourceLifecycleRestoreService restoreService,
        CancellationToken cancellationToken)
    {
        var preview = await restoreService.PreviewRestoreAsync(RestoreRequest(resourceId, request, restoredAt: null), cancellationToken);
        return Results.Ok(new OperationResultDto<ResourceLifecycleRestorePreviewSummary, ResourceLifecycleRestoreCandidateResult>(
            preview.ToSummary(),
            preview.Candidates));
    }

    private static async Task<IResult> RestoreAsync(
        string resourceId,
        LifecycleRestoreRequestDto request,
        IResourceLifecycleRestoreService restoreService,
        CancellationToken cancellationToken)
    {
        var result = await restoreService.RestoreAsync(RestoreRequest(resourceId, request, DateTimeOffset.UtcNow), cancellationToken);
        return Results.Ok(new OperationResultDto<ResourceLifecycleRestoreApplicationSummary, ResourceLifecycleRestoreCandidateResult>(
            result.ToSummary(),
            result.Candidates));
    }

    private static ResourceLifecycleRestoreRequest RestoreRequest(
        string resourceId,
        LifecycleRestoreRequestDto request,
        DateTimeOffset? restoredAt) => new()
    {
        Candidates = [new ResourceLifecycleRestoreCandidate { ResourceId = resourceId, ExpectedState = request.ExpectedState }],
        RestoredAt = restoredAt,
    };

    private static async Task<IResult> GetSchemaStatusAsync(
        string resourceId,
        IResourceManager resourceManager,
        IResourceDefinitionStore definitionStore,
        IResourceSchemaVersionService schemaService,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetLatestVersionAsync(resourceId, cancellationToken) is not { } latest)
            return ResourceNotFound(resourceId);

        var status = await schemaService.GetSchemaStatusAsync(latest, cancellationToken);

        // The upgrade reports carried-forward aspect keys only after it has written the new version. Showing them
        // beforehand means repeating its rule: payload keys the target definition does not declare.
        var definition = await definitionStore.GetDefinitionAsync(latest.DefinitionId, cancellationToken);
        var undeclared = definition is null
            ? []
            : latest.Aspects.Keys
                .Where(key => !definition.AspectDefinitions.ContainsKey(key))
                .Order(StringComparer.Ordinal)
                .ToList();

        return Results.Ok(new SchemaStatusDto(
            status.ResourceId,
            status.ResourceVersion,
            status.DefinitionId,
            status.RecordedDefinitionVersion,
            status.LatestDefinitionVersion,
            status.Status,
            status.Message,
            undeclared));
    }

    private static async Task<IResult> UpgradeSchemaAsync(
        string resourceId,
        SchemaUpgradeRequestDto request,
        IResourceSchemaVersionService schemaService,
        CancellationToken cancellationToken)
    {
        // Upgrade failures are exceptions rather than result diagnostics; map each onto problem details.
        try
        {
            var result = await schemaService.UpgradeAsync(resourceId, new ResourceSchemaUpgradeRequest { BaseVersion = request.BaseVersion }, cancellationToken);
            return Results.Ok(new SchemaUpgradeResultDto(
                result.Status,
                result.Resource?.Version,
                result.SourceDefinitionVersion,
                result.TargetDefinitionVersion,
                result.CarriedForwardAspectKeys,
                result.Message));
        }
        catch (VersionNotFoundException)
        {
            // Thrown only when the resource has no versions at all.
            return ResourceNotFound(resourceId);
        }
        catch (ConcurrencyException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "The resource changed since it was read.", [new DiagnosticDto("concurrency-conflict", null, null, exception.Message)]);
        }
        catch (ResourceSchemaUpgradeException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "The schema upgrade was rejected.", [new DiagnosticDto(exception.Code, null, null, exception.Message)]);
        }
    }

    private static async Task<IResult> GetPoliciesAsync(
        string definitionId,
        IResourceDefinitionStore definitionStore,
        IResourcePolicyValidator validator,
        CancellationToken cancellationToken)
    {
        if (await definitionStore.GetDefinitionAsync(definitionId, cancellationToken) is not { } definition)
            return DefinitionNotFound(definitionId);

        return Results.Ok(new DefinitionPoliciesDto(
            definition.DefinitionId,
            definition.Version,
            definition.PolicyDeclarations,
            validator.Validate(definition).Diagnostics));
    }

    private static async Task<IResult> PreviewPoliciesAsync(
        string definitionId,
        PolicyPreviewRequestDto request,
        IResourceDefinitionStore definitionStore,
        IResourcePolicyEvaluationService evaluationService,
        CancellationToken cancellationToken)
    {
        if (await definitionStore.GetDefinitionAsync(definitionId, cancellationToken) is null)
            return DefinitionNotFound(definitionId);

        var preview = await evaluationService.PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            DefinitionIds = [definitionId],
            EvaluationTimestamp = request.EvaluationTimestamp ?? DateTimeOffset.UtcNow,
        }, cancellationToken);

        return Results.Ok(new PolicyPreviewDto(
            preview.ToSummary(),
            preview.Candidates.Select(candidate => new PolicyPreviewCandidateDto(
                candidate.PolicyId,
                candidate.PolicyKind,
                candidate.Outcome,
                candidate.ResourceId,
                candidate.ResourceVersion,
                candidate.Reason)).ToList(),
            preview.Diagnostics));
    }

    private static async Task<IResult> ApplyPoliciesAsync(
        PolicyApplicationRequestDto request,
        IResourcePolicyApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        if (request.Candidates is not { Count: > 0 })
            return Problem(StatusCodes.Status400BadRequest, "Select at least one candidate.", [new DiagnosticDto("candidates-required", null, null, "Select at least one candidate.")]);

        var result = await applicationService.ApplyAsync(new ResourcePolicyApplicationRequest
        {
            Candidates = [.. request.Candidates],
            AppliedAt = DateTimeOffset.UtcNow,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        }, cancellationToken);

        return Results.Ok(new OperationResultDto<ResourcePolicyApplicationSummary, ResourcePolicyApplicationCandidateResult>(
            result.ToSummary(),
            result.Candidates));
    }

    private static async Task<IResult> PruneVersionsAsync(
        PolicyPruningRequestDto request,
        IResourcePolicyPruningApplicationService pruningService,
        CancellationToken cancellationToken)
    {
        if (request.Candidates is not { Count: > 0 })
            return Problem(StatusCodes.Status400BadRequest, "Select at least one candidate.", [new DiagnosticDto("candidates-required", null, null, "Select at least one candidate.")]);

        var result = await pruningService.ApplyAsync(new ResourcePolicyPruningApplicationRequest
        {
            Candidates = [.. request.Candidates],
            AppliedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return Results.Ok(new OperationResultDto<ResourcePolicyPruningApplicationSummary, ResourcePolicyPruningApplicationCandidateResult>(
            result.ToSummary(),
            result.Candidates));
    }

    private static IResult ResourceNotFound(string resourceId) => NotFound("resource-not-found", $"Resource '{resourceId}' was not found.");

    private static IResult DefinitionNotFound(string definitionId) => NotFound("definition-not-found", $"Definition '{definitionId}' was not found.");

    private static IResult NotFound(string code, string message) =>
        Problem(StatusCodes.Status404NotFound, "Not found", [new DiagnosticDto(code, null, null, message)], message);

    private static IResult Problem<TDiagnostic>(int statusCode, string title, IReadOnlyList<TDiagnostic> diagnostics, string? detail = null) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["diagnostics"] = diagnostics });
}
