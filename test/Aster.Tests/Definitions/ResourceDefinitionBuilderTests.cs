using Aster.Core.Definitions;
using Aster.Core.Exceptions;

namespace Aster.Tests.Definitions;

public sealed class ResourceDefinitionBuilderTests
{
    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);
    private sealed record TagAspect(string Value);

    [Fact]
    public void Build_WithValidDefinition_ReturnsDefinition()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Product")
               .WithAspect<TitleAspect>()
               .WithAspect<PriceAspect>();

        // Act
        var definition = builder.Build();

        // Assert
        Assert.Equal("Product", definition.DefinitionId);
        Assert.NotNull(definition.Id);
        Assert.Equal(0, definition.Version); // store assigns final version
        Assert.Equal(2, definition.AspectDefinitions.Count);
        Assert.True(definition.AspectDefinitions.ContainsKey("TitleAspect"));
        Assert.True(definition.AspectDefinitions.ContainsKey("PriceAspect"));
    }

    [Fact]
    public void Build_WithNamedAspect_UsesCompositeKey()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Catalog")
               .WithNamedAspect<TagAspect>("Categories");

        // Act
        var definition = builder.Build();

        // Assert
        Assert.True(definition.AspectDefinitions.ContainsKey("TagAspect:Categories"));
        var aspect = definition.AspectDefinitions["TagAspect:Categories"];
        Assert.Equal("TagAspect", aspect.AspectDefinitionId);
        Assert.True(aspect.RequiresName);
    }

    [Fact]
    public void Build_WithDuplicateUnnamedAspect_ThrowsDuplicateAspectAttachmentException()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Product")
               .WithAspect<TitleAspect>()
               .WithAspect<TitleAspect>(); // duplicate

        // Act & Assert
        Assert.Throws<DuplicateAspectAttachmentException>(() => builder.Build());
    }

    [Fact]
    public void Build_WithDuplicateNamedAspect_ThrowsDuplicateAspectAttachmentException()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Catalog")
               .WithNamedAspect<TagAspect>("Featured")
               .WithNamedAspect<TagAspect>("Featured"); // duplicate named key

        // Act & Assert
        Assert.Throws<DuplicateAspectAttachmentException>(() => builder.Build());
    }

    [Fact]
    public void Build_SameTypeUnnamedAndNamed_DoesNotThrow()
    {
        // Arrange — unnamed "TagAspect" and named "TagAspect:Featured" are distinct keys
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Catalog")
               .WithAspect<TagAspect>()
               .WithNamedAspect<TagAspect>("Featured");

        // Act
        var definition = builder.Build();

        // Assert
        Assert.Equal(2, definition.AspectDefinitions.Count);
    }

    [Fact]
    public void Build_WithoutDefinitionId_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithAspect<TitleAspect>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_WithSingleton_SetsFlagOnDefinition()
    {
        // Arrange
        var builder = new ResourceDefinitionBuilder();
        builder.WithDefinitionId("Config")
               .WithSingleton();

        // Act
        var definition = builder.Build();

        // Assert
        Assert.True(definition.IsSingleton);
    }
}
