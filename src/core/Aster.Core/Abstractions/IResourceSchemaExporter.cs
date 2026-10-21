using System.Text.Json.Nodes;
using Aster.Core.Models.Definitions;

namespace Aster.Core.Abstractions;

/// <summary>
/// Exports resource definitions as JSON Schema 2020-12 documents (#106), so hosts with their own UIs can
/// generate forms with standard tooling (react-jsonschema-form, JSONForms, Blazor form generators) instead of
/// re-implementing facet metadata. Rendering stays a host concern; the schema is the contract.
/// </summary>
/// <remarks>
/// The schema is never stricter than save-time validation: a resource that Aster accepts also validates
/// against its exported schema. A few constraints are only partly expressible and are documented on
/// <c>wiki/Json-Schema-Export.md</c>: .NET versus ECMA-262 <c>pattern</c> dialects, string lengths counted in
/// UTF-16 code units by Aster but in code points by JSON Schema, and guid and datetime allowed values, which
/// Aster compares after normalization and are therefore exported as the <c>x-aster-allowedValues</c>
/// annotation rather than <c>enum</c>.
/// </remarks>
public interface IResourceSchemaExporter
{
    /// <summary>
    /// Builds the schema of a resource's aspects: one property per aspect attachment key, each an object with
    /// one property per facet. Output is deterministic: aspects are ordered by key (ordinal) and facets by
    /// <c>DisplayOrder</c>, then declaration order.
    /// </summary>
    /// <param name="definition">The resource definition to export.</param>
    /// <returns>A new JSON Schema 2020-12 document.</returns>
    JsonObject Export(ResourceDefinition definition);

    /// <summary>
    /// Builds the schema of one aspect's payload: an object with one property per facet, as used for each
    /// aspect property of <see cref="Export(ResourceDefinition)"/>. Useful for shared aspect definitions.
    /// </summary>
    /// <param name="aspect">The aspect definition to export.</param>
    /// <returns>A new JSON Schema 2020-12 document.</returns>
    JsonObject ExportAspect(AspectDefinition aspect);
}
