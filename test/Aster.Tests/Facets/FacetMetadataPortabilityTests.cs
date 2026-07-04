using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Portability;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Facets;

/// <summary>
/// US4: facet metadata rides portable snapshots losslessly — export/import preserves every
/// property, definitions without metadata produce snapshot content unchanged from today,
/// and import never re-runs metadata validation (lossless, unvalidated round-trip).
/// </summary>
public sealed class FacetMetadataPortabilityTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static ServiceProvider CreateProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    private sealed class TitleAspect
    {
        public string String { get; init; } = string.Empty;
    }

    private static ResourceDefinition BuildFullMetadataDefinition() =>
        new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithTypedFacet<string>()
            .WithFacetMetadata(new FacetMetadata
            {
                Label = "Title",
                Description = "The product title.",
                DisplayOrder = 5,
                DefaultValue = "alpha",
                AllowedValues = ["alpha", "beta", "gamma"],
                MinLength = 2,
                MaxLength = 32,
                Pattern = "^[a-z]+$",
                UiHint = "text",
            })
            .Build();

    [Fact]
    public async Task ExportImport_FullMetadata_RoundTripsEveryPropertyLosslessly()
    {
        // Arrange — register a full-metadata definition in the source store.
        await using var source = CreateProvider();
        await source.GetRequiredService<IResourceDefinitionStore>()
            .RegisterDefinitionAsync(BuildFullMetadataDefinition());

        var exported = await source.GetRequiredService<IResourcePortabilityService>().ExportAsync(
            new PortableSnapshotExportRequest
            {
                ScopeMode = PortableExportScopeMode.DefinitionsOnly,
                DefinitionIds = ["Product"],
            });
        Assert.NotNull(exported.Snapshot);

        // Act — import into a completely fresh store.
        await using var target = CreateProvider();
        var importResult = await target.GetRequiredService<IResourcePortabilityService>()
            .ImportAsync(exported.Snapshot);
        var imported = await target.GetRequiredService<IResourceDefinitionStore>()
            .GetDefinitionAsync("Product");

        // Assert — every metadata property survived the round-trip (SC-005).
        Assert.Equal(PortableImportStatus.Imported, importResult.Status);
        Assert.NotNull(imported);
        var facet = Assert.Single(imported.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal("Title", facet.Metadata.Label);
        Assert.Equal("The product title.", facet.Metadata.Description);
        Assert.Equal(5, facet.Metadata.DisplayOrder);
        Assert.Equal("alpha", facet.Metadata.DefaultValue);
        Assert.NotNull(facet.Metadata.AllowedValues);
        Assert.Equal(
            (IEnumerable<string>)["alpha", "beta", "gamma"],
            facet.Metadata.AllowedValues.Cast<string>());
        Assert.Equal(2, facet.Metadata.MinLength);
        Assert.Equal(32, facet.Metadata.MaxLength);
        Assert.Equal("^[a-z]+$", facet.Metadata.Pattern);
        Assert.Equal("text", facet.Metadata.UiHint);
        Assert.Null(facet.Metadata.Minimum);
        Assert.Null(facet.Metadata.Maximum);
    }

    [Fact]
    public async Task Export_DefinitionWithoutMetadata_SnapshotContentUnchangedFromToday()
    {
        // Arrange — a metadata-free definition.
        await using var source = CreateProvider();
        await source.GetRequiredService<IResourceDefinitionStore>()
            .RegisterDefinitionAsync(new ResourceDefinitionBuilder()
                .WithDefinitionId("Product")
                .WithTypedAspect<TitleAspect>()
                .WithTypedFacet<string>()
                .Build());

        // Act — export and serialize the snapshot exactly as a host would.
        var exported = await source.GetRequiredService<IResourcePortabilityService>().ExportAsync(
            new PortableSnapshotExportRequest
            {
                ScopeMode = PortableExportScopeMode.DefinitionsOnly,
                DefinitionIds = ["Product"],
            });
        Assert.NotNull(exported.Snapshot);
        var json = JsonSerializer.Serialize(exported.Snapshot, WebJsonOptions);

        // Assert — no metadata property appears anywhere in the serialized snapshot (FR-019).
        Assert.DoesNotContain("\"metadata\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_DefinitionWithInvalidMetadata_ImportsLosslesslyWithoutValidation()
    {
        // Arrange — a snapshot whose definition carries metadata that would be rejected at
        // registration (contradictory bounds and a malformed pattern).
        var invalidMetadata = new FacetMetadata
        {
            MinLength = 10,
            MaxLength = 2,
            Pattern = "[unclosed",
        };
        var snapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions =
            [
                new ResourceDefinitionBuilder()
                    .WithDefinitionId("Product")
                    .WithTypedAspect<TitleAspect>()
                    .WithTypedFacet<string>()
                    .WithFacetMetadata(invalidMetadata)
                    .Build() with
                {
                    Version = 1,
                },
            ],
        };

        // Act — import must not invoke the metadata validator (lossless, unvalidated).
        await using var target = CreateProvider();
        var result = await target.GetRequiredService<IResourcePortabilityService>().ImportAsync(snapshot);
        var imported = await target.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Product");

        // Assert — the import succeeded and the invalid metadata round-tripped byte-for-byte.
        Assert.Equal(PortableImportStatus.Imported, result.Status);
        Assert.NotNull(imported);
        var facet = Assert.Single(imported.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal(10, facet.Metadata.MinLength);
        Assert.Equal(2, facet.Metadata.MaxLength);
        Assert.Equal("[unclosed", facet.Metadata.Pattern);
    }
}
