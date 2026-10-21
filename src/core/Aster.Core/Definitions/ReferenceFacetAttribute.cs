using Aster.Core.Models.Definitions;

namespace Aster.Core.Definitions;

/// <summary>
/// Marks a property of a typed aspect as a reference to other resources, so facet derivation
/// (<see cref="ResourceDefinitionBuilder.WithAspect{T}()"/>, <see cref="AspectDefinitionBuilder.FromType{T}(bool)"/>)
/// declares a <c>reference</c> facet for a <see cref="string"/> property and a <c>reference[]</c> facet for a
/// collection of strings, instead of <c>string</c> and <c>string[]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Aster never infers references (ADR 0002 §8); this attribute is the explicit opt-in. The derived facet is the
/// same facet <c>WithFacet(id, "reference")</c> plus <c>WithFacetMetadata</c> would declare: the options below
/// become its <see cref="FacetMetadata.TargetDefinitionIds"/> and <see cref="FacetMetadata.ReferenceResolution"/>,
/// and are validated at registration, exported to JSON Schema and checked on save exactly as declared ones.
/// </para>
/// <para>
/// Deriving an aspect throws <see cref="InvalidOperationException"/> when the property is not a
/// <see cref="string"/> or a collection that derives <c>string[]</c>, or carries its own
/// <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record Agent
/// {
///     [ReferenceFacet("Prompt")]
///     public required string Prompt { get; init; }
///
///     [ReferenceFacet("Tool", ReferenceResolution = "production")]
///     public string[] Tools { get; init; } = [];
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ReferenceFacetAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReferenceFacetAttribute"/> class.
    /// </summary>
    /// <param name="targetDefinitionIds">
    /// The definition IDs a reference may point to, as <see cref="FacetMetadata.TargetDefinitionIds"/>. None means
    /// any definition.
    /// </param>
    public ReferenceFacetAttribute(params string[] targetDefinitionIds)
    {
        TargetDefinitionIds = targetDefinitionIds is null ? [] : [.. targetDefinitionIds];
    }

    /// <summary>
    /// Gets the definition IDs a reference may point to; empty when any definition is allowed.
    /// </summary>
    public IReadOnlyList<string> TargetDefinitionIds { get; }

    /// <summary>
    /// Gets or sets how a reference resolves to a target version, as <see cref="FacetMetadata.ReferenceResolution"/>:
    /// <see langword="null"/> or <c>"latest"</c> resolves the latest version; any other value names the activation
    /// channel whose active version is used.
    /// </summary>
    public string? ReferenceResolution { get; set; }
}
