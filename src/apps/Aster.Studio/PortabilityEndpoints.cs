using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;

namespace Aster.Studio;

/// <summary>
/// Snapshot export and import over <see cref="IResourcePortabilityService"/>. The tenant comes from the request, never
/// from the body: export reads the selected tenant and import writes into it, so moving content between tenants is
/// an export, a tenant switch and an import. Results keep the SDK's identity mappings and stable diagnostic codes.
/// </summary>
internal static class PortabilityEndpoints
{
    public static RouteGroupBuilder MapPortabilityApi(this RouteGroupBuilder api)
    {
        api.MapPost("/portability/export", ExportAsync)
            .WithName("ExportSnapshot");

        api.MapPost("/portability/preview", PreviewImportAsync)
            .WithName("PreviewSnapshotImport");

        api.MapPost("/portability/import", ImportAsync)
            .WithName("ImportSnapshot");

        return api;
    }

    private static async Task<IResult> ExportAsync(
        PortableSnapshotExportRequest request,
        HttpContext http,
        IResourcePortabilityService portability,
        CancellationToken cancellationToken)
    {
        request.TenantScope = http.GetTenantScope();
        var result = await portability.ExportAsync(request, cancellationToken);
        return Results.Ok(new PortabilityResultDto<PortableExportSummary, PortableSnapshotExportResult>(result.ToSummary(), result));
    }

    private static async Task<IResult> PreviewImportAsync(
        SnapshotImportRequestDto request,
        HttpContext http,
        IResourcePortabilityService portability,
        CancellationToken cancellationToken)
    {
        if (request.Snapshot is null)
            return SnapshotRequired();

        var preview = await portability.PreviewImportAsync(request.Snapshot, ImportOptions(request, http), cancellationToken);
        return Results.Ok(new PortabilityResultDto<PortableImportPreviewSummary, PortableImportPreview>(preview.ToSummary(), preview));
    }

    // Import re-plans against the target as it is now rather than trusting the preview, so its identity map is the
    // one that was written.
    private static async Task<IResult> ImportAsync(
        SnapshotImportRequestDto request,
        HttpContext http,
        IResourcePortabilityService portability,
        CancellationToken cancellationToken)
    {
        if (request.Snapshot is null)
            return SnapshotRequired();

        var result = await portability.ImportAsync(request.Snapshot, ImportOptions(request, http), cancellationToken);
        return Results.Ok(new PortabilityResultDto<PortableImportSummary, PortableImportResult>(result.ToSummary(), result));
    }

    private static PortableImportOptions ImportOptions(SnapshotImportRequestDto request, HttpContext http) => new()
    {
        TargetTenantScope = http.GetTenantScope(),
        CollisionMode = request.CollisionMode,
    };

    private static IResult SnapshotRequired() =>
        Results.BadRequest(new DiagnosticsEnvelope([new DiagnosticDto("snapshot-required", null, null, "Choose a snapshot file to import.")]));
}
