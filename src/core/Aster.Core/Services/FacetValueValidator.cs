using Aster.Core.Models.Definitions;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Save-time validation of present facet values against declared metadata constraints.
/// Invoked by <c>DefaultResourceManager</c> and <c>InMemoryResourceManager</c> create/update
/// paths before any before-save lifecycle hook and before any write, so a violation means
/// nothing was persisted. Absent or null values are skipped entirely — missing-value handling
/// remains governed exclusively by the existing <c>IsRequired</c> flag.
/// </summary>
public static class FacetValueValidator
{
    /// <summary>
    /// Validates every metadata-bearing facet's present value across all aspect attachments
    /// with a matching payload.
    /// </summary>
    /// <param name="definition">The resolved resource definition.</param>
    /// <param name="aspects">The resource's aspect payloads keyed by attachment key.</param>
    /// <returns>
    /// All violations, ordered by aspect key, then facet definition id, then code (ordinal);
    /// empty when every present value conforms.
    /// </returns>
    public static IReadOnlyList<FacetValueDiagnostic> Validate(
        ResourceDefinition definition,
        IReadOnlyDictionary<string, object> aspects)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(aspects);

        var diagnostics = new List<FacetValueDiagnostic>();

        foreach (var (aspectKey, aspectDefinition) in definition.AspectDefinitions)
        {
            if (!aspects.TryGetValue(aspectKey, out var payload) || payload is null)
                continue;

            foreach (var facet in aspectDefinition.FacetDefinitions)
            {
                if (facet.Metadata is null)
                    continue;

                ValidateFacetValue(aspectKey, facet, facet.Metadata, payload, diagnostics);
            }
        }

        if (diagnostics.Count > 1)
        {
            diagnostics.Sort(static (left, right) =>
            {
                var byAspect = string.CompareOrdinal(left.AspectKey, right.AspectKey);
                if (byAspect != 0)
                    return byAspect;

                var byFacet = string.CompareOrdinal(left.FacetDefinitionId, right.FacetDefinitionId);
                return byFacet != 0 ? byFacet : string.CompareOrdinal(left.Code, right.Code);
            });
        }

        return diagnostics;
    }

    private static void ValidateFacetValue(
        string aspectKey,
        FacetDefinition facet,
        FacetMetadata metadata,
        object payload,
        List<FacetValueDiagnostic> diagnostics)
    {
        var facetType = ClassifyFacetType(facet.Type);
        if (facetType == FacetValueType.Unknown)
            return;

        // Absent or null values skip all constraint checks (IsRequired semantics unchanged).
        if (!AspectFacetValueResolver.TryResolveValue(payload, facet.FacetDefinitionId, out var rawValue))
            return;

        // Values that cannot be normalized to the declared type are skipped (no new rejection
        // channel in this slice; type enforcement is a candidate future slice).
        if (!TryNormalizeValue(rawValue, facetType, out var value))
            return;

        void Add(string code, string message) =>
            diagnostics.Add(new FacetValueDiagnostic(code, message, aspectKey, facet.FacetDefinitionId));

        // AllowedValues.
        if (facetType != FacetValueType.Bool && metadata.AllowedValues is { Count: > 0 } allowedValues)
        {
            var allowed = false;
            foreach (var entry in allowedValues)
            {
                if (TryNormalizeValue(entry, facetType, out var normalizedEntry) && normalizedEntry.Equals(value))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                Add(FacetValueDiagnosticCodes.NotAllowed, "Value is not in the declared AllowedValues list.");
        }

        // String length bounds and pattern.
        if (facetType == FacetValueType.String && value is string text)
        {
            if ((metadata.MinLength is { } minLength && text.Length < minLength)
                || (metadata.MaxLength is { } maxLength && text.Length > maxLength))
            {
                Add(FacetValueDiagnosticCodes.LengthOutOfRange,
                    $"Value length ({text.Length}) is outside the declared length bounds.");
            }

            if (metadata.Pattern is { } pattern)
            {
                bool matches;
                try
                {
                    matches = EvaluatePattern(text, pattern);
                }
                catch (ArgumentException)
                {
                    // A malformed pattern cannot be evaluated; registration is the
                    // well-formedness gate, so skip the check for this value.
                    matches = true;
                }

                if (!matches)
                    Add(FacetValueDiagnosticCodes.PatternMismatch, "Value does not match the declared pattern.");
            }
        }

        // Numeric range bounds (integer comparison for int facets, decimal for decimal facets;
        // decimal represents every long exactly, so one comparison path serves both).
        if (facetType is FacetValueType.Int or FacetValueType.Decimal)
        {
            var numeric = value switch
            {
                long integer => (decimal)integer,
                decimal decimalValue => decimalValue,
                _ => (decimal?)null,
            };

            if (numeric is { } numericValue
                && ((metadata.Minimum is { } minimum && numericValue < minimum)
                    || (metadata.Maximum is { } maximum && numericValue > maximum)))
            {
                Add(FacetValueDiagnosticCodes.OutOfRange,
                    $"Value ({numericValue}) is outside the declared numeric range.");
            }
        }
    }
}
