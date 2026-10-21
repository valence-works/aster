using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Tests.Services;

public sealed class IdentifierValidatorTests
{
    [Theory]
    [InlineData(" product")]
    [InlineData("product ")]
    [InlineData(" product ")]
    [InlineData("\tproduct")]
    [InlineData("product\r\n")]
    [InlineData(" product")]
    [InlineData("product ")]
    [InlineData(" ")]
    public void HasSurroundingWhitespace_LeadingOrTrailingWhitespace_IsTrue(string value) =>
        Assert.True(IdentifierValidator.HasSurroundingWhitespace(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("product")]
    [InlineData("product one")]
    [InlineData("a\tb")]
    [InlineData("x")]
    public void HasSurroundingWhitespace_NoLeadingOrTrailingWhitespace_IsFalse(string? value) =>
        Assert.False(IdentifierValidator.HasSurroundingWhitespace(value));

    [Fact]
    public void ThrowIfSurroundingWhitespace_PaddedValue_ThrowsArgumentExceptionWithStableCode()
    {
        var resourceId = "product-1 ";

        var exception = Assert.Throws<InvalidIdentifierException>(() => IdentifierValidator.ThrowIfSurroundingWhitespace(resourceId));

        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal("identifier-whitespace", exception.Code);
        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, exception.Code);
        Assert.Equal("resourceId", exception.ParamName);
        Assert.Equal("product-1 ", exception.Identifier);
        Assert.Contains("'product-1 '", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("product-1")]
    public void ThrowIfSurroundingWhitespace_UnpaddedValue_DoesNotThrow(string? value) =>
        IdentifierValidator.ThrowIfSurroundingWhitespace(value);

    [Fact]
    public void ThrowIfSurroundingWhitespace_ExplicitParamName_IsReported()
    {
        var exception = Assert.Throws<InvalidIdentifierException>(() => IdentifierValidator.ThrowIfSurroundingWhitespace(" x", "channel"));

        Assert.Equal("channel", exception.ParamName);
    }

    [Fact]
    public void InvalidIdentifierException_DefaultConstructors_UseSurroundingWhitespaceCode()
    {
        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, new InvalidIdentifierException().Code);
        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, new InvalidIdentifierException("message").Code);
        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, new InvalidIdentifierException("message", new InvalidOperationException()).Code);
    }

    [Theory]
    [InlineData(" tenant-a")]
    [InlineData("tenant-a ")]
    [InlineData("\ttenant-a")]
    public void TenantScopeFromTenantId_PaddedTenantId_Throws(string tenantId)
    {
        var exception = Assert.Throws<InvalidIdentifierException>(() => TenantScope.FromTenantId(tenantId));

        Assert.Equal(InvalidIdentifierException.SurroundingWhitespaceCode, exception.Code);
        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void TenantScopeFromTenantId_BlankTenantId_StillThrowsPlainArgumentException()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => TenantScope.FromTenantId("  "));

        Assert.IsNotType<InvalidIdentifierException>(exception);
    }

    [Fact]
    public void TenantScopeFromTenantId_InnerWhitespace_IsAccepted() =>
        Assert.Equal("tenant a", TenantScope.FromTenantId("tenant a").TenantId);

    [Fact]
    public void ResourceDefinitionBuilder_PaddedDefinitionId_Throws()
    {
        var exception = Assert.Throws<InvalidIdentifierException>(() => new ResourceDefinitionBuilder().WithDefinitionId("Product "));

        Assert.Equal("id", exception.ParamName);
    }
}
