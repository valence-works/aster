using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;

namespace Aster.Samples.PromptRegistry.Portability;

/// <summary>
/// Moves a prompt set (agents with the prompts and tools they reference) from one registry
/// environment to another as a JSON portable snapshot.
/// </summary>
public sealed class PromptSetTransfer(IResourcePortabilityService portability)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Exports <paramref name="agentIds"/> with every prompt and tool they reference
    /// (<see cref="PortableSnapshotExportRequest.IncludeReferencedResources"/>): all versions, their
    /// activation state, and the definitions they use.
    /// </summary>
    /// <returns>The snapshot as JSON, ready to copy to another environment.</returns>
    /// <exception cref="InvalidOperationException">The export reported errors.</exception>
    public async Task<string> ExportAsync(IEnumerable<string> agentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        var result = await portability.ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = [.. agentIds],
            IncludeReferencedResources = true,
        }, cancellationToken);

        var errors = result.Diagnostics.Where(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error).ToList();
        if (result.Snapshot is null || errors.Count > 0)
            throw new InvalidOperationException($"Export failed: {string.Join("; ", errors.Select(static error => $"{error.Code}: {error.Message}"))}");

        return JsonSerializer.Serialize(result.Snapshot, JsonOptions);
    }

    /// <summary>
    /// Plans an import without writing anything. Items whose IDs already exist here with different
    /// content are remapped to new IDs, and the references to them are rewritten.
    /// </summary>
    public async Task<PortableImportPreview> PreviewAsync(string snapshotJson, CancellationToken cancellationToken = default) =>
        await portability.PreviewImportAsync(Read(snapshotJson), Options, cancellationToken);

    /// <summary>Imports the snapshot with the same remapping as <see cref="PreviewAsync"/>.</summary>
    public async Task<PortableImportResult> ImportAsync(string snapshotJson, CancellationToken cancellationToken = default) =>
        await portability.ImportAsync(Read(snapshotJson), Options, cancellationToken);

    private static PortableImportOptions Options => new() { CollisionMode = PortableImportCollisionMode.RemapDivergent };

    private static PortableSnapshot Read(string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        return JsonSerializer.Deserialize<PortableSnapshot>(snapshotJson, JsonOptions)
            ?? throw new ArgumentException("The snapshot JSON is empty.", nameof(snapshotJson));
    }
}
