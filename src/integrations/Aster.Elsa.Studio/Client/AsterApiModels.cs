using System.Text.Json.Nodes;

namespace Aster.Elsa.Studio.Client;

/// <summary>The latest version of a resource definition, as <c>GET /definitions</c> lists it.</summary>
/// <param name="DefinitionId">The definition ID.</param>
/// <param name="Version">The latest definition version.</param>
/// <param name="AspectKeys">The aspect attachment keys, in key order.</param>
/// <param name="IsSingleton">Whether the definition allows only one resource.</param>
internal sealed record AsterDefinitionSummary(string DefinitionId, int Version, IReadOnlyList<string> AspectKeys, bool IsSingleton);

/// <summary>One resource version, as the resource endpoints return it.</summary>
/// <param name="ResourceId">The resource ID.</param>
/// <param name="DefinitionId">The definition ID.</param>
/// <param name="DefinitionVersion">The definition version the resource was saved against, when known.</param>
/// <param name="Version">The resource version.</param>
/// <param name="Created">When the version was created.</param>
/// <param name="Owner">The owner, when set.</param>
/// <param name="Aspects">The aspect payloads by attachment key.</param>
internal sealed record AsterResourceDocument(
    string ResourceId,
    string DefinitionId,
    int? DefinitionVersion,
    int Version,
    DateTime Created,
    string? Owner,
    JsonObject Aspects);

/// <summary>A page of resources from <c>GET /resources</c> or <c>POST /resources/query</c>.</summary>
/// <param name="Items">The resources.</param>
/// <param name="HasMore">Whether another page exists.</param>
internal sealed record AsterResourcePageResult(IReadOnlyList<AsterResourceDocument> Items, bool HasMore);

/// <summary>One entry of <c>GET /resources/{id}/versions</c>.</summary>
/// <param name="Version">The resource version.</param>
/// <param name="Created">When the version was created.</param>
/// <param name="IsLatest">Whether it is the latest version.</param>
/// <param name="IsDraft">Whether it is active in no channel.</param>
/// <param name="ActiveChannels">The channels it is active in.</param>
/// <param name="LifecycleState">The lifecycle state as the server names it, such as <c>none</c> or <c>archived</c>.</param>
internal sealed record AsterVersionSummary(
    int Version,
    DateTime Created,
    bool IsLatest,
    bool IsDraft,
    IReadOnlyList<string> ActiveChannels,
    string LifecycleState);

/// <summary>One diagnostic from a <c>400</c> ProblemDetails or <c>POST /resources/query/validate</c>.</summary>
/// <param name="Code">The stable code, such as <c>facet-value-required</c>.</param>
/// <param name="Message">The human-readable explanation.</param>
/// <param name="AspectKey">The aspect attachment key, when the diagnostic concerns one.</param>
/// <param name="FacetId">The facet ID, when the diagnostic concerns one.</param>
/// <param name="ElementIndex">The array element index, when the diagnostic concerns one element.</param>
/// <param name="Path">The request-body path, such as <c>filter.and[1].op</c>, when the server sets one.</param>
/// <param name="Feature">The query feature, for query diagnostics.</param>
internal sealed record AsterDiagnostic(
    string Code,
    string Message,
    string? AspectKey = null,
    string? FacetId = null,
    int? ElementIndex = null,
    string? Path = null,
    string? Feature = null);

/// <summary>A ProblemDetails response from the Aster endpoints.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Title">The problem title.</param>
/// <param name="Detail">The problem detail.</param>
/// <param name="Code">The <c>code</c> extension of a <c>409</c>, such as <c>concurrency-conflict</c>.</param>
/// <param name="Diagnostics">The <c>diagnostics</c> extension of a <c>400</c>.</param>
internal sealed record AsterProblem(int Status, string? Title, string? Detail, string? Code, IReadOnlyList<AsterDiagnostic> Diagnostics)
{
    /// <summary>Gets a one-line summary for an alert.</summary>
    public string Summary => Detail ?? Title ?? $"The Aster endpoint returned {Status}.";
}

/// <summary>The result of <c>POST /resources/query/validate</c>.</summary>
/// <param name="IsValid">Whether the query would run.</param>
/// <param name="Diagnostics">Every reason it would not, with request-body paths.</param>
internal sealed record AsterQueryValidationResult(bool IsValid, IReadOnlyList<AsterDiagnostic> Diagnostics);

/// <summary>
/// The parts of <c>GET /resources/query/capabilities</c> the module uses. Names are normalized (lower case, without
/// separators), so a host's enum naming policy doesn't matter.
/// </summary>
/// <param name="MetadataOperators">The comparison operators metadata filters support.</param>
/// <param name="MetadataContainsFields">The metadata fields that support <c>contains</c>.</param>
/// <param name="SupportsMetadataSorting">Whether metadata sorting is supported.</param>
internal sealed record AsterQueryCapabilities(
    IReadOnlySet<string> MetadataOperators,
    IReadOnlySet<string> MetadataContainsFields,
    bool SupportsMetadataSorting);

/// <summary>A resource the reference picker offers.</summary>
/// <param name="ResourceId">The resource ID, which is the reference value.</param>
/// <param name="DefinitionId">The resource's definition.</param>
internal sealed record AsterReferenceCandidate(string ResourceId, string DefinitionId);

/// <summary>A resource whose reference facet names another resource.</summary>
/// <param name="ResourceId">The referring resource.</param>
/// <param name="DefinitionId">The referring resource's definition.</param>
/// <param name="Via">The <c>aspectKey.facetId</c> facets that hold the reference.</param>
internal sealed record AsterReferrer(string ResourceId, string DefinitionId, IReadOnlyList<string> Via);

/// <summary>Thrown when an Aster endpoint returns an error status.</summary>
internal sealed class AsterApiException : Exception
{
    public AsterApiException(AsterProblem problem)
        : base(problem.Summary)
    {
        Problem = problem;
    }

    /// <summary>Gets the problem the endpoint returned.</summary>
    public AsterProblem Problem { get; }
}
