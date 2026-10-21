using System.Text.Json;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Microsoft.AspNetCore.Http;

namespace Aster.AspNetCore;

/// <summary>
/// One validation diagnostic in a <c>400</c> ProblemDetails response, under the <c>diagnostics</c> extension.
/// </summary>
/// <param name="Code">The stable diagnostic code, such as <c>facet-value-required</c>.</param>
/// <param name="AspectKey">The aspect attachment key, when the diagnostic concerns one.</param>
/// <param name="FacetId">The facet ID, when the diagnostic concerns one.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="ElementIndex">The array element index, when the diagnostic concerns one element.</param>
public sealed record AsterDiagnostic(string Code, string? AspectKey, string? FacetId, string Message, int? ElementIndex = null)
{
    /// <summary>
    /// Gets the location in the request body the diagnostic concerns, such as <c>filter.and[1].op</c>, when it
    /// concerns one. Query diagnostics set it.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets the query feature a query diagnostic concerns, such as <c>paging</c> or <c>sort</c>, as the query validator
    /// names it.
    /// </summary>
    public string? Feature { get; init; }
}

/// <summary>
/// Maps Aster outcomes and exceptions onto ProblemDetails responses: validation failures to <c>400</c> with
/// <c>diagnostics</c>, missing entities to <c>404</c>, and concurrency and uniqueness conflicts to <c>409</c>.
/// </summary>
internal static class AsterProblems
{
    public static IResult Validation(string title, IEnumerable<AsterDiagnostic> diagnostics) =>
        new ProblemResult(StatusCodes.Status400BadRequest, title, detail: null, "diagnostics", diagnostics.ToList());

    public static IResult Validation(string code, string message) =>
        Validation(message, [new AsterDiagnostic(code, null, null, message)]);

    public static IResult NotFound(string detail) =>
        new ProblemResult(StatusCodes.Status404NotFound, "Not found", detail);

    public static IResult Conflict(string code, string detail) =>
        new ProblemResult(StatusCodes.Status409Conflict, "Conflict", detail, "code", code);

    public static IResult Forbidden() =>
        new ProblemResult(StatusCodes.Status403Forbidden, "Forbidden", detail: null);

    public static IResult UnsupportedMediaType() =>
        new ProblemResult(StatusCodes.Status415UnsupportedMediaType, "Unsupported media type", "The request body must be JSON (application/json).");

    public static IResult? FromException(Exception exception) => exception switch
    {
        FacetValueValidationException e => Validation(
            "Facet values are invalid.",
            e.Diagnostics.Select(static d => new AsterDiagnostic(d.Code, d.AspectKey, d.FacetDefinitionId, d.Message, d.ElementIndex))),
        FacetMetadataValidationException e => Validation(
            "Facet metadata is invalid.",
            e.Diagnostics.Select(static d => new AsterDiagnostic(d.Code, d.AspectKey, d.FacetDefinitionId, d.Message))),
        SharedAspectResolutionException e => Validation(
            "Shared aspects could not be resolved.",
            e.Diagnostics.Select(static d => new AsterDiagnostic(d.Code, d.AttachmentKey, null, d.Message))),
        UnsupportedQueryFeatureException e => Validation(e.Message, [new AsterDiagnostic(e.Code, null, null, e.Message) { Feature = e.Feature }]),
        TenantScopeException e => Validation(e.Code, e.Message),
        InvalidIdentifierException e => Validation(e.Code, e.Message),
        DuplicateAspectAttachmentException e => Validation("duplicate-aspect-attachment", e.Message),
        ConcurrencyException e => Conflict("concurrency-conflict", e.Message),
        DuplicateResourceIdException e => Conflict("duplicate-resource-id", e.Message),
        SingletonViolationException e => Conflict("singleton-violation", e.Message),
        LifecycleHookException e => Conflict(e.Code, e.Message),
        VersionNotFoundException e => NotFound(e.Message),
        ResourceSchemaUpgradeException e => SchemaUpgradeRejected(e),
        _ => null,
    };

    /// <summary>
    /// Maps a rejected schema upgrade by its first code: a missing resource is <c>404</c>, a stale base version is
    /// <c>409</c> <c>concurrency-conflict</c>, and anything else (an invalid target or an invalid upgraded payload) is
    /// <c>400</c> with every diagnostic.
    /// </summary>
    private static IResult SchemaUpgradeRejected(ResourceSchemaUpgradeException exception) => exception.Code switch
    {
        ResourceSchemaUpgradeDiagnosticCodes.ResourceNotFound => NotFound(exception.Message),
        ResourceSchemaUpgradeDiagnosticCodes.ConcurrencyConflict => Conflict(exception.Code, exception.Message),
        _ => Validation(
            "The schema upgrade was rejected.",
            exception.Diagnostics.Select(static d => new AsterDiagnostic(d.Code, d.AspectKey, d.FacetDefinitionId, d.Message, d.ElementIndex))),
    };
}

/// <summary>
/// A ProblemDetails response whose Aster extension member (<c>diagnostics</c> or <c>code</c>) is serialized with the
/// endpoint's own serializer options, so its shape does not depend on the host's JSON options. The envelope still goes
/// through the host's ProblemDetails pipeline (<c>AddProblemDetails</c> customizations apply), and its standard members
/// have fixed names.
/// </summary>
internal sealed class ProblemResult(int statusCode, string title, string? detail, string? extensionName = null, object? extensionValue = null)
    : IResult, IStatusCodeHttpResult
{
    public int? StatusCode => statusCode;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var extensions = extensionName is null
            ? null
            : new Dictionary<string, object?> { [extensionName] = JsonSerializer.SerializeToElement(extensionValue, AsterJson.Options(httpContext)) };
        return TypedResults.Problem(statusCode: statusCode, title: title, detail: detail, extensions: extensions).ExecuteAsync(httpContext);
    }
}

/// <summary>
/// Turns Aster exceptions thrown by an endpoint into ProblemDetails responses; any other exception propagates.
/// </summary>
internal sealed class AsterExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception exception) when (AsterProblems.FromException(exception) is { } problem)
        {
            return problem;
        }
    }
}
