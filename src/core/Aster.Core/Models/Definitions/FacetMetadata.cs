using System.Text.Json.Serialization;

namespace Aster.Core.Models.Definitions;

/// <summary>
/// Optional display and validation metadata declared on a <see cref="FacetDefinition"/>.
/// Every property is optional; an absent (<see langword="null"/>) property means "not declared".
/// Undeclared properties are omitted from JSON so definitions without metadata serialize
/// exactly as before this feature.
/// </summary>
public sealed record FacetMetadata
{
    /// <summary>
    /// Display label for form generation. Display-only; never affects validation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }

    /// <summary>
    /// Human-readable description. Display-only; never affects validation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    /// Plain integer with host-defined ordering semantics; uniqueness and contiguity are not enforced.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayOrder { get; init; }

    /// <summary>
    /// Metadata-only default for form pre-population; never injected into saved resource versions.
    /// Must satisfy the facet's declared type and all declared constraints.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Closed list of permitted values. When declared: non-empty, no duplicates (after normalization),
    /// and every entry must match the facet's declared type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<object>? AllowedValues { get; init; }

    /// <summary>
    /// Minimum string length in UTF-16 code units. String facets only; non-negative.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; init; }

    /// <summary>
    /// Maximum string length in UTF-16 code units. String facets only; non-negative;
    /// must be greater than or equal to <see cref="MinLength"/> when both are declared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; init; }

    /// <summary>
    /// Inclusive numeric lower bound. Int and decimal facets only; whole numbers required for int facets.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Minimum { get; init; }

    /// <summary>
    /// Inclusive numeric upper bound. Int and decimal facets only; whole numbers required for int facets;
    /// must be greater than or equal to <see cref="Minimum"/> when both are declared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Maximum { get; init; }

    /// <summary>
    /// .NET regular-expression pattern. String facets only; must be well-formed at registration;
    /// evaluated with a bounded match timeout at save time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pattern { get; init; }

    /// <summary>
    /// Minimum number of elements. Array facets only; non-negative.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinItems { get; init; }

    /// <summary>
    /// Maximum number of elements. Array facets only; non-negative;
    /// must be greater than or equal to <see cref="MinItems"/> when both are declared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxItems { get; init; }

    /// <summary>
    /// When <see langword="true"/>, elements must be distinct after normalization to the element type.
    /// Array facets only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UniqueItems { get; init; }

    /// <summary>
    /// Definition IDs a reference may point to. Reference facets only; when declared: non-empty,
    /// with no blank or duplicate entries. Undeclared means any definition.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? TargetDefinitionIds { get; init; }

    /// <summary>
    /// How a reference resolves to a target version. Reference facets only. <see langword="null"/> or
    /// <c>"latest"</c> (case-insensitive) resolves the latest version; any other value names the activation
    /// channel whose active version is used. Must not be blank when declared.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReferenceResolution { get; init; }

    /// <summary>
    /// Opaque, non-validated string carried verbatim for host interpretation
    /// (e.g., "text", "multiline", "select"). Never affects validation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UiHint { get; init; }
}
