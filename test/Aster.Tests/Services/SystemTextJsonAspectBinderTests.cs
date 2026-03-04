using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Services;

namespace Aster.Tests.Services;

public sealed class SystemTextJsonAspectBinderTests
{
    private readonly SystemTextJsonAspectBinder aspectBinder = new();
    private readonly SystemTextJsonFacetBinder facetBinder = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Test POCOs
    // ──────────────────────────────────────────────────────────────────────────

    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);
    private sealed record RichAspect(string Text, int Count, bool Flag, decimal Value, DateTime Timestamp);

    // ──────────────────────────────────────────────────────────────────────────
    // Aspect binder: Serialize → Deserialize round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AspectBinder_StringProperty_RoundTrips()
    {
        // Arrange
        var original = new TitleAspect("Super Gadget");

        // Act
        var serialized = aspectBinder.Serialize(original);
        var result = aspectBinder.Deserialize<TitleAspect>(serialized);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Super Gadget", result.Title);
    }

    [Fact]
    public void AspectBinder_DecimalAndStringProperties_RoundTrip()
    {
        // Arrange
        var original = new PriceAspect(99.99m, "USD");

        // Act
        var serialized = aspectBinder.Serialize(original);
        var result = aspectBinder.Deserialize<PriceAspect>(serialized);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(99.99m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void AspectBinder_AllPrimitiveTypes_RoundTrip()
    {
        // Arrange
        var timestamp = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var original = new RichAspect("hello", 42, true, 3.14m, timestamp);

        // Act
        var serialized = aspectBinder.Serialize(original);
        var result = aspectBinder.Deserialize<RichAspect>(serialized);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hello", result.Text);
        Assert.Equal(42, result.Count);
        Assert.True(result.Flag);
        Assert.Equal(3.14m, result.Value);
        Assert.Equal(timestamp, result.Timestamp);
    }

    [Fact]
    public void AspectBinder_NullInput_ReturnsDefault()
    {
        // Act
        var result = aspectBinder.Deserialize<TitleAspect>(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AspectBinder_AlreadyCorrectType_ReturnsFastPath()
    {
        // Arrange — raw value is already the target type
        var original = new TitleAspect("Already typed");

        // Act
        var result = aspectBinder.Deserialize<TitleAspect>(original);

        // Assert
        Assert.Same(original, result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // State Replace semantics via ResourceExtensions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetAspect_ReplacesEntireAspect_StateReplace()
    {
        // Arrange
        var resource = new Resource
        {
            ResourceId = "r1",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>
            {
                ["TitleAspect"] = new TitleAspect("Original"),
                ["PriceAspect"] = new PriceAspect(10m, "USD"),
            }
        };

        var updated = new TitleAspect("Replaced Title");

        // Act — only TitleAspect is replaced; PriceAspect must be untouched
        var newResource = resource.SetAspect("TitleAspect", updated, aspectBinder);

        // Assert — State Replace: TitleAspect is new POCO
        var retrieved = newResource.GetAspect<TitleAspect>("TitleAspect", aspectBinder);
        Assert.NotNull(retrieved);
        Assert.Equal("Replaced Title", retrieved.Title);

        // PriceAspect must remain intact
        var price = newResource.GetAspect<PriceAspect>("PriceAspect", aspectBinder);
        Assert.NotNull(price);
        Assert.Equal(10m, price.Amount);
    }

    [Fact]
    public void SetAspect_OriginalResourceIsImmutable()
    {
        // Arrange
        var resource = new Resource
        {
            ResourceId = "r1",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object> { ["TitleAspect"] = new TitleAspect("Original") }
        };

        // Act
        var newResource = resource.SetAspect("TitleAspect", new TitleAspect("Changed"), aspectBinder);

        // Assert — original resource is unchanged
        var originalTitle = resource.GetAspect<TitleAspect>("TitleAspect", aspectBinder);
        Assert.NotNull(originalTitle);
        Assert.Equal("Original", originalTitle.Title);
        Assert.NotSame(resource, newResource);
    }

    [Fact]
    public void GetAspect_MissingKey_ReturnsDefault()
    {
        // Arrange
        var resource = new Resource
        {
            ResourceId = "r1",
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
        };

        // Act
        var result = resource.GetAspect<TitleAspect>("NonExistent", aspectBinder);

        // Assert
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Facet binder: round-trip via AspectInstanceExtensions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FacetBinder_DecimalValue_RoundTrips()
    {
        // Arrange
        var instance = new AspectInstance
        {
            AspectDefinitionId = "Price",
            Facets = new Dictionary<string, object> { ["Amount"] = 99.99m }
        };

        // Act
        var amount = instance.GetFacet<decimal>("Amount", facetBinder);

        // Assert
        Assert.Equal(99.99m, amount);
    }

    [Fact]
    public void FacetBinder_SetFacet_ReplacesIndividualFacet()
    {
        // Arrange
        var instance = new AspectInstance
        {
            AspectDefinitionId = "Price",
            Facets = new Dictionary<string, object>
            {
                ["Amount"] = "10.00",
                ["Currency"] = "USD",
            }
        };

        // Act — replace only Amount
        var updated = instance.SetFacet("Amount", 250m, facetBinder);
        var newAmount = updated.GetFacet<decimal>("Amount", facetBinder);
        var currency = updated.GetFacet<string>("Currency", facetBinder);

        // Assert
        Assert.Equal(250m, newAmount);
        Assert.Equal("USD", currency);
    }

    [Fact]
    public void FacetBinder_OriginalInstanceIsImmutable()
    {
        // Arrange
        var instance = new AspectInstance
        {
            AspectDefinitionId = "Price",
            Facets = new Dictionary<string, object> { ["Amount"] = "10.00" }
        };

        // Act
        var updated = instance.SetFacet("Amount", 999m, facetBinder);

        // Assert — original is unchanged
        var originalAmount = instance.GetFacet<string>("Amount", facetBinder);
        Assert.Equal("10.00", originalAmount);
        Assert.NotSame(instance, updated);
    }
}
