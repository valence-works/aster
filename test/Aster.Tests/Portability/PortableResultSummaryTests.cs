using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;

namespace Aster.Tests.Portability;

public sealed class PortableResultSummaryTests
{
    [Fact]
    public void ExportSummary_WithSnapshot_AggregatesSnapshotSkippedAndDiagnostics()
    {
        var result = new PortableSnapshotExportResult
        {
            SourceTenantScope = TenantScope.FromTenantId("tenant-a"),
            Snapshot = Snapshot(),
            SkippedActivationEntries = [SkippedActivation()],
            Diagnostics =
            [
                Diagnostic(PortableDiagnosticSeverity.Warning, "z-code"),
                Diagnostic(PortableDiagnosticSeverity.Error, "a-code"),
                Diagnostic(PortableDiagnosticSeverity.Warning, "z-code"),
                Diagnostic(PortableDiagnosticSeverity.Info, ""),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(result.SourceTenantScope, summary.SourceTenantScope);
        Assert.True(summary.HasSnapshot);
        Assert.True(summary.HasErrors);
        Assert.Equal(1, summary.DefinitionCount);
        Assert.Equal(2, summary.ResourceVersionCount);
        Assert.Equal(1, summary.ActivationEntryCount);
        Assert.Equal(1, summary.LifecycleMarkerCount);
        Assert.Equal(1, summary.SkippedActivationEntryCount);
        Assert.Equal(
            [(PortableDiagnosticSeverity.Info, 1), (PortableDiagnosticSeverity.Warning, 2), (PortableDiagnosticSeverity.Error, 1)],
            summary.DiagnosticSeverityCounts.Select(static count => (count.Severity, count.Count)).ToList());
        Assert.Equal(
            [("a-code", 1), ("z-code", 2)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void ExportSummary_NullSnapshotAndCollections_AreTreatedAsEmptyForCounts()
    {
        var summary = new PortableSnapshotExportResult
        {
            Snapshot = null,
            Diagnostics = null!,
            SkippedActivationEntries = null!,
        }.ToSummary();

        Assert.False(summary.HasSnapshot);
        Assert.False(summary.HasErrors);
        Assert.Equal(0, summary.DefinitionCount);
        Assert.Equal(0, summary.ResourceVersionCount);
        Assert.Equal(0, summary.ActivationEntryCount);
        Assert.Equal(0, summary.LifecycleMarkerCount);
        Assert.Equal(0, summary.SkippedActivationEntryCount);
        Assert.Empty(summary.DiagnosticSeverityCounts);
        Assert.Empty(summary.DiagnosticCodeCounts);
    }

    [Fact]
    public void ImportPreviewSummary_AggregatesPlannedCountsMappingsAndDiagnostics()
    {
        var preview = new PortableImportPreview
        {
            SourceTenantScope = TenantScope.FromTenantId("source"),
            TargetTenantScope = TenantScope.FromTenantId("target"),
            CanImport = false,
            Counts = new PortablePlannedImportCounts
            {
                Definitions = 1,
                Resources = 2,
                ResourceVersions = 3,
                ActivationEntries = 4,
                LifecycleMarkers = 5,
                ReusedIdenticalItems = 6,
                RemappedItems = 7,
            },
            IdentityMap =
            [
                Mapping(PortableIdentityMappingReason.Preserved),
                Mapping(PortableIdentityMappingReason.RemappedDivergent),
                Mapping(PortableIdentityMappingReason.RemappedDivergent),
            ],
            Diagnostics =
            [
                Diagnostic(PortableDiagnosticSeverity.Error, PortableDiagnosticCodes.DivergentIdentityCollision),
            ],
        };

        var summary = preview.ToSummary();

        Assert.Equal(preview.SourceTenantScope, summary.SourceTenantScope);
        Assert.Equal(preview.TargetTenantScope, summary.TargetTenantScope);
        Assert.False(summary.CanImport);
        Assert.True(summary.HasErrors);
        Assert.Same(preview.Counts, summary.Counts);
        Assert.Equal(15, summary.TotalPlannedItemCount);
        Assert.Equal(
            [(PortableIdentityMappingReason.Preserved, 1), (PortableIdentityMappingReason.RemappedDivergent, 2)],
            summary.MappingReasonCounts.Select(static count => (count.Reason, count.Count)).ToList());
        Assert.Equal(
            [(PortableDiagnosticSeverity.Error, 1)],
            summary.DiagnosticSeverityCounts.Select(static count => (count.Severity, count.Count)).ToList());
        Assert.Equal(
            [(PortableDiagnosticCodes.DivergentIdentityCollision, 1)],
            summary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void ImportSummary_AggregatesActualCountsMappingsStatusAndDiagnostics()
    {
        var imported = new PortableImportResult
        {
            SourceTenantScope = TenantScope.FromTenantId("source"),
            TargetTenantScope = TenantScope.FromTenantId("target"),
            Status = PortableImportStatus.Imported,
            Counts = new PortableActualImportCounts
            {
                Definitions = 1,
                Resources = 1,
                ResourceVersions = 2,
                ActivationEntries = 1,
                LifecycleMarkers = 1,
                ReusedIdenticalItems = 3,
                RemappedItems = 2,
            },
            IdentityMap =
            [
                Mapping(PortableIdentityMappingReason.ReusedIdentical),
                Mapping(PortableIdentityMappingReason.RemappedDivergent),
            ],
        };

        var noOp = imported with { Status = PortableImportStatus.NoOp };
        var failed = imported with
        {
            Status = PortableImportStatus.Failed,
            Diagnostics = [Diagnostic(PortableDiagnosticSeverity.Error, PortableDiagnosticCodes.ImportApplyFailed)],
        };

        var importedSummary = imported.ToSummary();
        var noOpSummary = noOp.ToSummary();
        var failedSummary = failed.ToSummary();

        Assert.Equal(imported.SourceTenantScope, importedSummary.SourceTenantScope);
        Assert.Equal(imported.TargetTenantScope, importedSummary.TargetTenantScope);
        Assert.True(importedSummary.IsImported);
        Assert.False(importedSummary.IsNoOp);
        Assert.False(importedSummary.IsFailed);
        Assert.Equal(6, importedSummary.TotalActualItemCount);
        Assert.Same(imported.Counts, importedSummary.Counts);
        Assert.Equal(
            [(PortableIdentityMappingReason.ReusedIdentical, 1), (PortableIdentityMappingReason.RemappedDivergent, 1)],
            importedSummary.MappingReasonCounts.Select(static count => (count.Reason, count.Count)).ToList());
        Assert.True(noOpSummary.IsNoOp);
        Assert.False(noOpSummary.IsFailed);
        Assert.True(failedSummary.IsFailed);
        Assert.True(failedSummary.HasErrors);
        Assert.Equal(
            [(PortableDiagnosticSeverity.Error, 1)],
            failedSummary.DiagnosticSeverityCounts.Select(static count => (count.Severity, count.Count)).ToList());
        Assert.Equal(
            [(PortableDiagnosticCodes.ImportApplyFailed, 1)],
            failedSummary.DiagnosticCodeCounts.Select(static count => (count.Code, count.Count)).ToList());
    }

    [Fact]
    public void Summaries_NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ((PortableSnapshotExportResult)null!).ToSummary());
        Assert.Throws<ArgumentNullException>(() => ((PortableImportPreview)null!).ToSummary());
        Assert.Throws<ArgumentNullException>(() => ((PortableImportResult)null!).ToSummary());
    }

    [Fact]
    public void ImportSummaries_NullCollectionsAndCounts_AreTreatedAsEmptyForCounts()
    {
        var previewSummary = new PortableImportPreview
        {
            CanImport = true,
            Counts = null!,
            IdentityMap = null!,
            Diagnostics = null!,
        }.ToSummary();
        var importSummary = new PortableImportResult
        {
            Status = PortableImportStatus.Imported,
            Counts = null!,
            IdentityMap = null!,
            Diagnostics = null!,
        }.ToSummary();

        Assert.Equal(0, previewSummary.TotalPlannedItemCount);
        Assert.Empty(previewSummary.MappingReasonCounts);
        Assert.Empty(previewSummary.DiagnosticSeverityCounts);
        Assert.Empty(previewSummary.DiagnosticCodeCounts);
        Assert.False(previewSummary.HasErrors);
        Assert.Equal(0, importSummary.TotalActualItemCount);
        Assert.Empty(importSummary.MappingReasonCounts);
        Assert.Empty(importSummary.DiagnosticSeverityCounts);
        Assert.Empty(importSummary.DiagnosticCodeCounts);
        Assert.False(importSummary.HasErrors);
    }

    private static PortableSnapshot Snapshot() =>
        new()
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            SourceTenantScope = TenantScope.FromTenantId("tenant-a"),
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "Product",
                    Id = "Product:1",
                    Version = 1,
                },
            ],
            Resources =
            [
                Resource("product-1", 1),
                Resource("product-1", 2),
            ],
            ActivationStates =
            [
                new ActivationState
                {
                    ResourceId = "product-1",
                    Channel = "Published",
                    ActiveVersions = [2],
                    LastUpdated = DateTime.UtcNow,
                },
            ],
            LifecycleMarkers =
            [
                new ResourceLifecycleMarker
                {
                    ResourceId = "product-1",
                    State = ResourceLifecycleMarkerState.Archived,
                    MarkedAt = DateTimeOffset.UtcNow,
                },
            ],
        };

    private static Resource Resource(string resourceId, int version) =>
        new()
        {
            ResourceId = resourceId,
            Id = $"{resourceId}:{version}",
            DefinitionId = "Product",
            Version = version,
            Created = DateTime.UtcNow,
        };

    private static SkippedActivationEntry SkippedActivation() =>
        new()
        {
            ResourceId = "product-1",
            Channel = "Published",
            Version = 1,
            Reason = SkippedActivationReason.ExcludedByResourceVersionScope,
        };

    private static PortableIdentityMapping Mapping(PortableIdentityMappingReason reason) =>
        new()
        {
            EntityKind = PortableEntityKind.Resource,
            SourceId = $"source-{reason}",
            TargetId = $"target-{reason}",
            Reason = reason,
        };

    private static PortableDiagnostic Diagnostic(PortableDiagnosticSeverity severity, string code) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = code,
        };
}
