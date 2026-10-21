namespace Aster.Core.Models.Definitions;

/// <summary>
/// A single registration-time facet metadata self-validation failure.
/// </summary>
/// <param name="Code">Stable diagnostic code; one of <see cref="FacetMetadataDiagnosticCodes"/>.</param>
/// <param name="Message">Human-readable, actionable explanation.</param>
/// <param name="AspectKey">Attachment key of the offending aspect (e.g., <c>PriceAspect</c> or <c>PriceAspect:Sale</c>).</param>
/// <param name="FacetDefinitionId">Offending facet identifier.</param>
public sealed record FacetMetadataDiagnostic(string Code, string Message, string AspectKey, string FacetDefinitionId);

/// <summary>
/// Stable diagnostic codes for registration-time facet metadata self-validation failures.
/// </summary>
public static class FacetMetadataDiagnosticCodes
{
    /// <summary>
    /// <c>MinLength</c> exceeds <c>MaxLength</c>, a length bound is negative, <c>Minimum</c> exceeds
    /// <c>Maximum</c>, a numeric bound on an int facet is not a whole number, or an item count bound
    /// (<c>MinItems</c>, <c>MaxItems</c>) is negative or inverted.
    /// </summary>
    public const string RangeInvalid = "facet-metadata-range-invalid";

    /// <summary>
    /// A constraint is declared for a facet type outside the constraint applicability matrix.
    /// </summary>
    public const string ConstraintInapplicable = "facet-metadata-constraint-inapplicable";

    /// <summary>
    /// <c>AllowedValues</c> is declared but empty.
    /// </summary>
    public const string AllowedValuesEmpty = "facet-metadata-allowed-values-empty";

    /// <summary>
    /// <c>AllowedValues</c> contains duplicate entries after normalization to the declared type.
    /// </summary>
    public const string AllowedValuesDuplicate = "facet-metadata-allowed-values-duplicate";

    /// <summary>
    /// An <c>AllowedValues</c> entry does not normalize to the facet's declared type.
    /// </summary>
    public const string AllowedValueTypeMismatch = "facet-metadata-allowed-value-type-mismatch";

    /// <summary>
    /// <c>DefaultValue</c> violates the declared type or any declared constraint.
    /// </summary>
    public const string DefaultValueInvalid = "facet-metadata-default-value-invalid";

    /// <summary>
    /// <c>Pattern</c> is not a well-formed .NET regular expression.
    /// </summary>
    public const string PatternInvalid = "facet-metadata-pattern-invalid";

    /// <summary>
    /// The facet's declared <c>Type</c> is not a supported facet type descriptor.
    /// </summary>
    public const string TypeUnknown = "facet-type-unknown";
}

/// <summary>
/// A single save-time facet value validation failure.
/// </summary>
/// <param name="Code">Stable diagnostic code; one of <see cref="FacetValueDiagnosticCodes"/>.</param>
/// <param name="Message">Human-readable, actionable explanation.</param>
/// <param name="AspectKey">Attachment key of the offending aspect on the resource.</param>
/// <param name="FacetDefinitionId">Offending facet identifier.</param>
public sealed record FacetValueDiagnostic(string Code, string Message, string AspectKey, string FacetDefinitionId)
{
    /// <summary>
    /// Zero-based index of the offending element when the violation concerns one element of an
    /// array facet; <see langword="null"/> when it concerns the facet value as a whole.
    /// </summary>
    public int? ElementIndex { get; init; }
}

/// <summary>
/// Stable diagnostic codes for save-time facet value validation failures.
/// </summary>
public static class FacetValueDiagnosticCodes
{
    /// <summary>
    /// A present value is outside the declared <c>AllowedValues</c> list.
    /// </summary>
    public const string NotAllowed = "facet-value-not-allowed";

    /// <summary>
    /// A present string value is shorter than <c>MinLength</c> or longer than <c>MaxLength</c>.
    /// </summary>
    public const string LengthOutOfRange = "facet-value-length-out-of-range";

    /// <summary>
    /// A present numeric value is below <c>Minimum</c> or above <c>Maximum</c>.
    /// </summary>
    public const string OutOfRange = "facet-value-out-of-range";

    /// <summary>
    /// A present string value does not match <c>Pattern</c> (including bounded-evaluation timeout).
    /// </summary>
    public const string PatternMismatch = "facet-value-pattern-mismatch";

    /// <summary>
    /// A present, non-null value does not normalize to the facet's declared type.
    /// </summary>
    public const string TypeMismatch = "facet-value-type-mismatch";

    /// <summary>
    /// A required facet is missing or null on a present aspect instance, or a required array facet is empty.
    /// </summary>
    public const string Required = "facet-value-required";

    /// <summary>
    /// An array value has fewer elements than <c>MinItems</c> or more than <c>MaxItems</c>.
    /// </summary>
    public const string ItemsOutOfRange = "facet-value-items-out-of-range";

    /// <summary>
    /// An array value declared with <c>UniqueItems</c> contains duplicate elements after normalization.
    /// </summary>
    public const string ItemsNotUnique = "facet-value-items-not-unique";
}

/// <summary>
/// A shared aspect attachment that could not be resolved.
/// </summary>
/// <param name="Code">Stable code from <see cref="SharedAspectDiagnosticCodes"/>.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="AttachmentKey">The attachment key on the resource definition.</param>
/// <param name="AspectDefinitionId">The shared aspect definition ID.</param>
/// <param name="Version">The pinned version; <see langword="null"/> when the latest was requested.</param>
public sealed record SharedAspectDiagnostic(string Code, string Message, string AttachmentKey, string AspectDefinitionId, int? Version);

/// <summary>
/// Stable diagnostic codes for shared aspect resolution (ADR 0003).
/// </summary>
public static class SharedAspectDiagnosticCodes
{
    /// <summary>The aspect definition, or the pinned version, does not exist in the tenant.</summary>
    public const string AspectDefinitionNotFound = "aspect-definition-not-found";

    /// <summary>The stored aspect has <c>RequiresName</c>, and the attachment is unnamed.</summary>
    public const string AspectNameRequired = "aspect-name-required";

    /// <summary>The selected resource definition does not pin an older version of the aspect.</summary>
    public const string AspectUpgradeNotApplicable = "aspect-upgrade-not-applicable";

    /// <summary>
    /// Another writer registered a version of the resource definition after the upgrade read it, so the upgrade
    /// wrote nothing. Preview again and retry.
    /// </summary>
    public const string DefinitionVersionConflict = "definition-version-conflict";
}
