using Aster.Core.Exceptions;

namespace Aster.Elsa;

/// <summary>
/// Maps Aster exceptions onto <see cref="AsterActivityException"/> with the codes <c>Aster.AspNetCore</c> uses for the
/// same exceptions (see its <c>AsterProblems.FromException</c>), so a workflow and an HTTP client see the same codes.
/// Keep the two in step.
/// </summary>
internal static class AsterFaults
{
    /// <summary>Runs <paramref name="operation"/>, turning an Aster exception into an <see cref="AsterActivityException"/>.</summary>
    public static async ValueTask<T> RunAsync<T>(Func<ValueTask<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (FromException(exception) is { } fault)
        {
            throw fault;
        }
    }

    /// <summary>Runs <paramref name="operation"/>, turning an Aster exception into an <see cref="AsterActivityException"/>.</summary>
    public static async ValueTask RunAsync(Func<ValueTask> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (FromException(exception) is { } fault)
        {
            throw fault;
        }
    }

    /// <summary>The fault for an Aster exception, or <see langword="null"/> for any other exception, which propagates as is.</summary>
    public static AsterActivityException? FromException(Exception exception) => exception switch
    {
        AsterActivityException => null,
        FacetValueValidationException e => Many(
            e.Diagnostics.Select(static d => new AsterActivityDiagnostic(d.Code, d.Message, d.AspectKey, d.FacetDefinitionId, d.ElementIndex)),
            AsterActivityFaultCodes.FacetValueInvalid,
            e),
        FacetMetadataValidationException e => Many(
            e.Diagnostics.Select(static d => new AsterActivityDiagnostic(d.Code, d.Message, d.AspectKey, d.FacetDefinitionId)),
            "facet-metadata-invalid",
            e),
        SharedAspectResolutionException e => Many(
            e.Diagnostics.Select(static d => new AsterActivityDiagnostic(d.Code, d.Message, d.AttachmentKey)),
            "shared-aspect-unresolved",
            e),
        UnsupportedQueryFeatureException e => new AsterActivityException(
            [new AsterActivityDiagnostic(e.Code, e.Message, Path: e.Path, Feature: e.Feature)], e.Message, e),
        TenantScopeException e => new AsterActivityException(e.Code, e.Message, e),
        InvalidIdentifierException e => new AsterActivityException(e.Code, e.Message, e),
        DuplicateAspectAttachmentException e => new AsterActivityException(AsterActivityFaultCodes.DuplicateAspectAttachment, e.Message, e),
        ConcurrencyException e => new AsterActivityException(AsterActivityFaultCodes.ConcurrencyConflict, e.Message, e),
        DuplicateResourceIdException e => new AsterActivityException(AsterActivityFaultCodes.DuplicateResourceId, e.Message, e),
        SingletonViolationException e => new AsterActivityException(AsterActivityFaultCodes.SingletonViolation, e.Message, e),
        LifecycleHookException e => new AsterActivityException(e.Code, e.Message, e),
        VersionNotFoundException e => new AsterActivityException(AsterActivityFaultCodes.VersionNotFound, e.Message, e),
        ResourceSchemaUpgradeException e => Many(
            e.Diagnostics.Select(static d => new AsterActivityDiagnostic(d.Code, d.Message, d.AspectKey, d.FacetDefinitionId, d.ElementIndex)),
            e.Code,
            e),
        _ => null,
    };

    private static AsterActivityException Many(IEnumerable<AsterActivityDiagnostic> diagnostics, string fallbackCode, Exception exception)
    {
        var list = diagnostics.ToList();
        if (list.Count == 0)
            list.Add(new AsterActivityDiagnostic(fallbackCode, exception.Message));
        return new AsterActivityException(list, exception.Message, exception);
    }
}
