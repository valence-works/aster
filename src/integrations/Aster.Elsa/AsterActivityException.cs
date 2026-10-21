using System.Text.Json;
using Elsa.Workflows;

namespace Aster.Elsa;

/// <summary>
/// One diagnostic of an <see cref="AsterActivityException"/>. Its members match the <c>diagnostics</c> entries that
/// <c>Aster.AspNetCore</c> returns over HTTP, so a client sees the same codes from a workflow and from an endpoint.
/// </summary>
/// <param name="Code">The stable diagnostic code, such as <c>facet-value-required</c>.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="AspectKey">The aspect attachment key, when the diagnostic concerns one.</param>
/// <param name="FacetId">The facet ID, when the diagnostic concerns one.</param>
/// <param name="ElementIndex">The array element index, when the diagnostic concerns one element.</param>
/// <param name="Path">The location in the query the diagnostic concerns, such as <c>Filter.Operands[1].Operator</c>.</param>
/// <param name="Feature">The query feature a query diagnostic concerns, such as <c>paging</c>.</param>
public sealed record AsterActivityDiagnostic(
    string Code,
    string Message,
    string? AspectKey = null,
    string? FacetId = null,
    int? ElementIndex = null,
    string? Path = null,
    string? Feature = null);

/// <summary>
/// Faults an Aster activity with a stable Aster code. Elsa records it as an activity incident; the incident's exception
/// metadata carries <c>code</c> and, as a JSON array, <c>diagnostics</c>.
/// </summary>
public sealed class AsterActivityException : Exception, ISafeExceptionMetadataProvider
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AsterActivityException"/> class with one diagnostic.
    /// </summary>
    /// <param name="code">The stable code; see <see cref="AsterActivityFaultCodes"/> and Aster's diagnostic code classes.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="innerException">The Aster exception the fault was mapped from, if any.</param>
    public AsterActivityException(string code, string message, Exception? innerException = null)
        : this([new AsterActivityDiagnostic(code, message)], message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsterActivityException"/> class with every diagnostic of a
    /// failure. <see cref="Code"/> is the first diagnostic's code.
    /// </summary>
    /// <param name="diagnostics">The diagnostics; at least one.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="innerException">The Aster exception the fault was mapped from, if any.</param>
    public AsterActivityException(IReadOnlyList<AsterActivityDiagnostic> diagnostics, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
            throw new ArgumentException("At least one diagnostic is required.", nameof(diagnostics));

        Diagnostics = [.. diagnostics];
        Code = Diagnostics[0].Code;
    }

    /// <summary>Gets the stable code of the fault: the first diagnostic's code.</summary>
    public string Code { get; }

    /// <summary>Gets every diagnostic of the fault, such as each invalid facet value.</summary>
    public IReadOnlyList<AsterActivityDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Returns the metadata Elsa stores with the incident: <c>code</c>, and <c>diagnostics</c> as a JSON array.
    /// </summary>
    /// <returns>The incident metadata.</returns>
    public IReadOnlyDictionary<string, string> GetSafeMetadata() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["code"] = Code,
        ["diagnostics"] = JsonSerializer.Serialize(Diagnostics, MetadataOptions),
    };
}

/// <summary>
/// Fault codes the Aster activities add to Aster's own diagnostic codes. Codes that <c>Aster.AspNetCore</c> or Aster
/// Studio also return have the same value.
/// </summary>
public static class AsterActivityFaultCodes
{
    /// <summary>The resource does not exist in the current tenant.</summary>
    public const string ResourceNotFound = "resource-not-found";

    /// <summary>The resource version does not exist in the current tenant.</summary>
    public const string VersionNotFound = "version-not-found";

    /// <summary>The resource definition does not exist in the current tenant.</summary>
    public const string DefinitionNotFound = "definition-not-found";

    /// <summary>The <c>BaseVersion</c> of an update is no longer the latest version.</summary>
    public const string ConcurrencyConflict = "concurrency-conflict";

    /// <summary>A caller-supplied resource ID is already in use.</summary>
    public const string DuplicateResourceId = "duplicate-resource-id";

    /// <summary>A singleton definition already has a resource.</summary>
    public const string SingletonViolation = "singleton-violation";

    /// <summary>An aspect key is attached to a definition more than once.</summary>
    public const string DuplicateAspectAttachment = "duplicate-aspect-attachment";

    /// <summary>A required definition ID input is empty.</summary>
    public const string DefinitionIdRequired = "definition-id-required";

    /// <summary>A required resource ID input is empty.</summary>
    public const string ResourceIdRequired = "resource-id-required";

    /// <summary>A required channel input is empty.</summary>
    public const string ChannelRequired = "channel-required";

    /// <summary>The aspects input is not a JSON object of aspect payloads keyed by aspect key.</summary>
    public const string AspectsInvalid = "aspects-invalid";

    /// <summary>Get Resource was given both a version and a channel; set at most one.</summary>
    public const string ResourceSelectorInvalid = "resource-selector-invalid";

    /// <summary>Promote found no version active in the source channel.</summary>
    public const string PromoteSourceNotActive = "promote-source-not-active";

    /// <summary>Promote found more than one version active in the source channel, so the version to promote is ambiguous.</summary>
    public const string PromoteSourceAmbiguous = "promote-source-ambiguous";

    /// <summary>A facet value failed validation and the exception carried no diagnostics.</summary>
    public const string FacetValueInvalid = "facet-value-invalid";
}
