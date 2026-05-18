using Aster.Core.Models.Querying;
using Aster.Core.Services;

namespace Aster.Tests.Querying;

public sealed class IndexProjectionDeclarationTests
{
    private readonly IndexProjectionValidator validator = new();

    [Fact]
    public void Validate_AcceptsSupportedMetadataAndFacetProjectionDeclarations()
    {
        var projections = new[]
        {
            IndexProjection.Metadata("resource_id", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Metadata("created", "Created", IndexFieldType.DateTime),
            IndexProjection.Facet("title", "Title", "Title", IndexFieldType.NormalizedText),
            IndexProjection.Facet("body", "Content", "Body", IndexFieldType.Text),
            IndexProjection.Facet("active", "Flags", "IsActive", IndexFieldType.Boolean),
            IndexProjection.Facet("count", "Metrics", "Count", IndexFieldType.Integer),
            IndexProjection.Facet("amount", "Price", "Amount", IndexFieldType.Decimal),
            IndexProjection.Facet("external_id", "External", "Id", IndexFieldType.Guid),
            IndexProjection.Facet("tags", "Taxonomy", "Tags", IndexFieldType.KeywordArray),
        };

        var result = validator.Validate(projections);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
        Assert.All(projections, projection => Assert.False(string.IsNullOrWhiteSpace(projection.FieldName)));
        Assert.True(projections.Single(projection => projection.FieldName == "tags").IsMultiValue);
    }

    [Fact]
    public void Validate_RejectsInvalidProjectionDeclarations()
    {
        var projections = new[]
        {
            IndexProjection.Metadata("", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Metadata("duplicate", "ResourceId", IndexFieldType.Keyword),
            IndexProjection.Facet("duplicate", "Title", "Title", IndexFieldType.Keyword),
            new IndexProjection(
                "missing-metadata",
                IndexProjectionSource.Metadata(""),
                IndexFieldType.Keyword),
            new IndexProjection(
                "metadata-with-facet-data",
                new IndexProjectionSource(
                    IndexProjectionSourceKind.Metadata,
                    MetadataField: "ResourceId",
                    AspectKey: "Title",
                    FacetKey: "Title"),
                IndexFieldType.Keyword),
            new IndexProjection(
                "missing-facet",
                IndexProjectionSource.Facet("", ""),
                IndexFieldType.Keyword),
            new IndexProjection(
                "facet-with-metadata-data",
                new IndexProjectionSource(
                    IndexProjectionSourceKind.Facet,
                    MetadataField: "ResourceId",
                    AspectKey: "Title",
                    FacetKey: "Title"),
                IndexFieldType.Keyword),
            new IndexProjection(
                "unknown-source",
                new IndexProjectionSource((IndexProjectionSourceKind)999),
                IndexFieldType.Keyword),
        };

        var result = validator.Validate(projections);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Code == IndexProjectionFailureCodes.InvalidProjectionDeclaration);
        Assert.Contains(result.Failures, failure => failure.Code == IndexProjectionFailureCodes.DuplicateProjectionField);
        Assert.All(result.Failures, failure => Assert.False(string.IsNullOrWhiteSpace(failure.Message)));
    }
}
