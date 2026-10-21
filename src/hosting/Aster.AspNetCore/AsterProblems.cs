using Aster.Core.Exceptions;
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
public sealed record AsterDiagnostic(string Code, string? AspectKey, string? FacetId, string Message, int? ElementIndex = null);

/// <summary>
/// Maps Aster outcomes and exceptions onto ProblemDetails responses: validation failures to <c>400</c> with
/// <c>diagnostics</c>, missing entities to <c>404</c>, and concurrency and uniqueness conflicts to <c>409</c>.
/// </summary>
internal static class AsterProblems
{
    public static IResult Validation(string title, IEnumerable<AsterDiagnostic> diagnostics) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: title,
            extensions: new Dictionary<string, object?> { ["diagnostics"] = diagnostics.ToList() });

    public static IResult Validation(string code, string message) =>
        Validation(message, [new AsterDiagnostic(code, null, null, message)]);

    public static IResult NotFound(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: detail);

    public static IResult Conflict(string code, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Forbidden() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

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
        UnsupportedQueryFeatureException e => Validation(e.Code, e.Message),
        TenantScopeException e => Validation(e.Code, e.Message),
        DuplicateAspectAttachmentException e => Validation("duplicate-aspect-attachment", e.Message),
        ConcurrencyException e => Conflict("concurrency-conflict", e.Message),
        DuplicateResourceIdException e => Conflict("duplicate-resource-id", e.Message),
        SingletonViolationException e => Conflict("singleton-violation", e.Message),
        LifecycleHookException e => Conflict(e.Code, e.Message),
        VersionNotFoundException e => NotFound(e.Message),
        _ => null,
    };
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
