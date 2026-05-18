using Aster.Core.Models.Querying;

namespace Aster.Core.Services;

/// <summary>
/// Validates provider-declared index projection definitions.
/// </summary>
public sealed class IndexProjectionValidator
{
    /// <summary>
    /// Validates an index projection declaration set.
    /// </summary>
    /// <param name="projections">The projection declarations.</param>
    /// <returns>The validation result.</returns>
    public IndexProjectionValidationResult Validate(IEnumerable<IndexProjection> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);

        var failures = new List<IndexProjectionFailure>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projection in projections)
        {
            ValidateProjection(projection, fieldNames, failures);
        }

        return IndexProjectionValidationResult.Create(failures);
    }

    private static void ValidateProjection(
        IndexProjection projection,
        HashSet<string> fieldNames,
        List<IndexProjectionFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(projection.FieldName))
        {
            failures.Add(Failure(
                projection,
                IndexProjectionFailureCodes.InvalidProjectionDeclaration,
                "Index projection field name must be non-empty."));
        }
        else if (!fieldNames.Add(projection.FieldName))
        {
            failures.Add(Failure(
                projection,
                IndexProjectionFailureCodes.DuplicateProjectionField,
                $"Index projection field name '{projection.FieldName}' is declared more than once."));
        }

        if (!Enum.IsDefined(projection.FieldType))
        {
            failures.Add(Failure(
                projection,
                IndexProjectionFailureCodes.InvalidProjectionDeclaration,
                $"Index projection field type '{projection.FieldType}' is not supported."));
        }

        ValidateSource(projection, failures);
    }

    private static void ValidateSource(IndexProjection projection, List<IndexProjectionFailure> failures)
    {
        switch (projection.Source.Kind)
        {
            case IndexProjectionSourceKind.Metadata:
                if (string.IsNullOrWhiteSpace(projection.Source.MetadataField)
                    || projection.Source.AspectKey is not null
                    || projection.Source.FacetKey is not null)
                {
                    failures.Add(Failure(
                        projection,
                        IndexProjectionFailureCodes.InvalidProjectionDeclaration,
                        "Metadata index projection sources require only a non-empty metadata field."));
                }

                break;

            case IndexProjectionSourceKind.Facet:
                if (string.IsNullOrWhiteSpace(projection.Source.AspectKey)
                    || string.IsNullOrWhiteSpace(projection.Source.FacetKey)
                    || projection.Source.MetadataField is not null)
                {
                    failures.Add(Failure(
                        projection,
                        IndexProjectionFailureCodes.InvalidProjectionDeclaration,
                        "Facet index projection sources require only a non-empty aspect key and facet key."));
                }

                break;

            default:
                failures.Add(Failure(
                    projection,
                    IndexProjectionFailureCodes.InvalidProjectionDeclaration,
                    $"Index projection source kind '{projection.Source.Kind}' is not supported."));
                break;
        }
    }

    private static IndexProjectionFailure Failure(IndexProjection projection, string code, string message) =>
        new(projection.FieldName, code, message, projection.Source.Describe());
}
