using System.Collections.ObjectModel;

namespace Aster.Core.Models.Querying;

/// <summary>
/// Declared value shape for an index projection.
/// </summary>
public enum IndexFieldType
{
    /// <summary>Exact-match scalar string value.</summary>
    Keyword,

    /// <summary>Human-readable string value.</summary>
    Text,

    /// <summary>Normalized scalar string value.</summary>
    NormalizedText,

    /// <summary>Boolean scalar value.</summary>
    Boolean,

    /// <summary>Integral numeric scalar value.</summary>
    Integer,

    /// <summary>Decimal numeric scalar value.</summary>
    Decimal,

    /// <summary>Date/time scalar value.</summary>
    DateTime,

    /// <summary>GUID scalar value.</summary>
    Guid,

    /// <summary>Multi-value exact-match string values.</summary>
    KeywordArray,
}

/// <summary>
/// Supported source kinds for index projections.
/// </summary>
public enum IndexProjectionSourceKind
{
    /// <summary>Resource metadata field source.</summary>
    Metadata,

    /// <summary>Resource aspect/facet source.</summary>
    Facet,
}

/// <summary>
/// Describes where an index projection reads its value from a resource version.
/// </summary>
/// <param name="Kind">The projection source kind.</param>
/// <param name="MetadataField">The metadata field name when <paramref name="Kind"/> is metadata.</param>
/// <param name="AspectKey">The aspect key when <paramref name="Kind"/> is facet.</param>
/// <param name="FacetKey">The facet key when <paramref name="Kind"/> is facet.</param>
public sealed record IndexProjectionSource(
    IndexProjectionSourceKind Kind,
    string? MetadataField = null,
    string? AspectKey = null,
    string? FacetKey = null)
{
    /// <summary>
    /// Creates a metadata projection source.
    /// </summary>
    /// <param name="metadataField">The resource metadata field name.</param>
    /// <returns>The metadata projection source.</returns>
    public static IndexProjectionSource Metadata(string metadataField) =>
        new(IndexProjectionSourceKind.Metadata, MetadataField: metadataField);

    /// <summary>
    /// Creates an aspect/facet projection source.
    /// </summary>
    /// <param name="aspectKey">The aspect key.</param>
    /// <param name="facetKey">The facet key.</param>
    /// <returns>The facet projection source.</returns>
    public static IndexProjectionSource Facet(string aspectKey, string facetKey) =>
        new(IndexProjectionSourceKind.Facet, AspectKey: aspectKey, FacetKey: facetKey);

    /// <summary>
    /// Returns a readable source description for diagnostics.
    /// </summary>
    /// <returns>The source description.</returns>
    public string Describe() => Kind switch
    {
        IndexProjectionSourceKind.Metadata => $"metadata '{MetadataField}'",
        IndexProjectionSourceKind.Facet => $"facet '{AspectKey}.{FacetKey}'",
        _ => $"source '{Kind}'",
    };
}

/// <summary>
/// Provider-declared mapping from a resource source to an index field.
/// </summary>
/// <param name="FieldName">Stable index field name.</param>
/// <param name="Source">The resource source for the projection.</param>
/// <param name="FieldType">The declared index field type.</param>
/// <param name="IsMultiValue">Whether the projection expects multiple values.</param>
public sealed record IndexProjection(
    string FieldName,
    IndexProjectionSource Source,
    IndexFieldType FieldType,
    bool IsMultiValue = false)
{
    /// <summary>
    /// Creates a metadata index projection.
    /// </summary>
    /// <param name="fieldName">Stable index field name.</param>
    /// <param name="metadataField">Resource metadata field name.</param>
    /// <param name="fieldType">Declared index field type.</param>
    /// <param name="isMultiValue">Whether the projection expects multiple values.</param>
    /// <returns>The index projection.</returns>
    public static IndexProjection Metadata(
        string fieldName,
        string metadataField,
        IndexFieldType fieldType,
        bool isMultiValue = false) =>
        new(fieldName, IndexProjectionSource.Metadata(metadataField), fieldType, IsMulti(fieldType, isMultiValue));

    /// <summary>
    /// Creates an aspect/facet index projection.
    /// </summary>
    /// <param name="fieldName">Stable index field name.</param>
    /// <param name="aspectKey">Aspect key.</param>
    /// <param name="facetKey">Facet key.</param>
    /// <param name="fieldType">Declared index field type.</param>
    /// <param name="isMultiValue">Whether the projection expects multiple values.</param>
    /// <returns>The index projection.</returns>
    public static IndexProjection Facet(
        string fieldName,
        string aspectKey,
        string facetKey,
        IndexFieldType fieldType,
        bool isMultiValue = false) =>
        new(fieldName, IndexProjectionSource.Facet(aspectKey, facetKey), fieldType, IsMulti(fieldType, isMultiValue));

    private static bool IsMulti(IndexFieldType fieldType, bool isMultiValue) =>
        fieldType == IndexFieldType.KeywordArray || isMultiValue;
}

/// <summary>
/// Successful value produced by index projection evaluation.
/// </summary>
/// <param name="FieldName">The projection field name.</param>
/// <param name="FieldType">The declared projection field type.</param>
/// <param name="Value">The typed projection value.</param>
public sealed record IndexProjectionValue(
    string FieldName,
    IndexFieldType FieldType,
    object Value);

/// <summary>
/// Combined result from evaluating index projections against a resource version.
/// </summary>
/// <param name="Values">Successful projection values.</param>
/// <param name="Failures">Structured per-projection failures.</param>
public sealed record IndexProjectionEvaluationResult(
    IReadOnlyList<IndexProjectionValue> Values,
    IReadOnlyList<IndexProjectionFailure> Failures)
{
    /// <summary>
    /// Gets a value indicating whether the projection evaluation produced no failures.
    /// </summary>
    public bool IsValid => Failures.Count == 0;

    /// <summary>
    /// Creates an evaluation result from mutable collections.
    /// </summary>
    /// <param name="values">Successful projection values.</param>
    /// <param name="failures">Structured projection failures.</param>
    /// <returns>The immutable evaluation result.</returns>
    public static IndexProjectionEvaluationResult Create(
        IEnumerable<IndexProjectionValue> values,
        IEnumerable<IndexProjectionFailure> failures) =>
        new(
            new ReadOnlyCollection<IndexProjectionValue>(values.ToList()),
            new ReadOnlyCollection<IndexProjectionFailure>(failures.ToList()));
}
