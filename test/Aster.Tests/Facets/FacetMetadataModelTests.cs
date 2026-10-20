using System.Text.Json;
using Aster.Core.Definitions;
using Aster.Core.InMemory;
using Aster.Core.Models.Definitions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Facets;

/// <summary>
/// US1: hosts declare optional facet metadata and read every declared property back
/// through the definition store; definitions without metadata behave exactly as today.
/// </summary>
public sealed class FacetMetadataModelTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static InMemoryResourceDefinitionStore CreateStore() =>
        new(NullLogger<InMemoryResourceDefinitionStore>.Instance);

    private sealed class PriceAspect
    {
        public decimal Decimal { get; init; }
    }

    private sealed class TitleAspect
    {
        public string String { get; init; } = string.Empty;
    }

    [Fact]
    public async Task RegisterDefinitionAsync_FullMetadata_RoundTripsEveryProperty()
    {
        // Arrange
        var store = CreateStore();
        var metadata = new FacetMetadata
        {
            Label = "Title",
            Description = "The product title shown in listings.",
            DisplayOrder = 10,
            DefaultValue = "unt",
            AllowedValues = ["unt", "untitled", "product"],
            MinLength = 2,
            MaxLength = 20,
            Pattern = "^[a-z]+$",
            UiHint = "text",
        };
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(metadata)
            .Build();

        // Act
        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(result);
        var facet = Assert.Single(result.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal("Title", facet.Metadata.Label);
        Assert.Equal("The product title shown in listings.", facet.Metadata.Description);
        Assert.Equal(10, facet.Metadata.DisplayOrder);
        Assert.Equal("unt", facet.Metadata.DefaultValue);
        Assert.NotNull(facet.Metadata.AllowedValues);
        Assert.Equal((IEnumerable<string>)["unt", "untitled", "product"], facet.Metadata.AllowedValues.Cast<string>());
        Assert.Equal(2, facet.Metadata.MinLength);
        Assert.Equal(20, facet.Metadata.MaxLength);
        Assert.Equal("^[a-z]+$", facet.Metadata.Pattern);
        Assert.Equal("text", facet.Metadata.UiHint);
        Assert.Null(facet.Metadata.Minimum);
        Assert.Null(facet.Metadata.Maximum);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_NumericMetadata_RoundTripsRangeBounds()
    {
        // Arrange
        var store = CreateStore();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<PriceAspect>()
            .WithFacetMetadata(new FacetMetadata
            {
                Label = "Price",
                Minimum = 0m,
                Maximum = 10_000m,
                DefaultValue = 0m,
                UiHint = "number",
            })
            .Build();

        // Act
        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(result);
        var facet = Assert.Single(result.AspectDefinitions["PriceAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal(0m, facet.Metadata.Minimum);
        Assert.Equal(10_000m, facet.Metadata.Maximum);
        Assert.Equal(0m, facet.Metadata.DefaultValue);
        Assert.Equal("number", facet.Metadata.UiHint);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_PartialMetadata_PreservesDeclaredAndLeavesRestNull()
    {
        // Arrange
        var store = CreateStore();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithFacetMetadata(new FacetMetadata
            {
                Label = "Title",
                UiHint = "multiline",
            })
            .Build();

        // Act
        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        // Assert
        Assert.NotNull(result);
        var facet = Assert.Single(result.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.NotNull(facet.Metadata);
        Assert.Equal("Title", facet.Metadata.Label);
        Assert.Equal("multiline", facet.Metadata.UiHint);
        Assert.Null(facet.Metadata.Description);
        Assert.Null(facet.Metadata.DisplayOrder);
        Assert.Null(facet.Metadata.DefaultValue);
        Assert.Null(facet.Metadata.AllowedValues);
        Assert.Null(facet.Metadata.MinLength);
        Assert.Null(facet.Metadata.MaxLength);
        Assert.Null(facet.Metadata.Minimum);
        Assert.Null(facet.Metadata.Maximum);
        Assert.Null(facet.Metadata.Pattern);
    }

    [Fact]
    public void Serialize_PartialMetadata_OmitsUndeclaredProperties()
    {
        // Arrange
        var facet = new FacetDefinition
        {
            FacetDefinitionId = "String",
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            Type = "string",
            Metadata = new FacetMetadata { Label = "Title" },
        };

        // Act
        var json = JsonSerializer.Serialize(facet, WebJsonOptions);

        // Assert
        Assert.Contains("\"label\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowedValues", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pattern", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uiHint", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterDefinitionAsync_NoMetadata_MetadataIsNullAndJsonUnchanged()
    {
        // Arrange
        var store = CreateStore();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .Build();

        // Act
        await store.RegisterDefinitionAsync(definition);
        var result = await store.GetDefinitionAsync("Product");

        // Assert — metadata is null after read-back
        Assert.NotNull(result);
        var facet = Assert.Single(result.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.Null(facet.Metadata);

        // Assert — serialized payload (web defaults, matching SqliteJsonResourceStore.Serialize)
        // contains no metadata property: byte-identical to today.
        var json = JsonSerializer.Serialize(result, WebJsonOptions);
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithFacetMetadata_NoFacetAdded_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect("TitleAspect");

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => builder.WithFacetMetadata(new FacetMetadata { Label = "Title" }));
    }

    [Fact]
    public void WithFacetMetadata_NoAspectAdded_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder().WithDefinitionId("Product");

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => builder.WithFacetMetadata(new FacetMetadata { Label = "Title" }));
    }

    [Fact]
    public void WithFacetMetadata_TargetsMostRecentlyAddedFacetOfMostRecentAspect()
    {
        // Arrange / Act
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithTypedAspect<TitleAspect>()
            .WithTypedAspect<PriceAspect>()
            .WithFacetMetadata(new FacetMetadata { Label = "Price" })
            .Build();

        // Assert — only the last facet of the last aspect carries metadata
        var titleFacet = Assert.Single(definition.AspectDefinitions["TitleAspect"].FacetDefinitions);
        Assert.Null(titleFacet.Metadata);
        var priceFacet = Assert.Single(definition.AspectDefinitions["PriceAspect"].FacetDefinitions);
        Assert.NotNull(priceFacet.Metadata);
        Assert.Equal("Price", priceFacet.Metadata.Label);
    }
}
