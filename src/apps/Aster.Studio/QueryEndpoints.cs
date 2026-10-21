using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Studio;

/// <summary>
/// The query builder's API: the active provider's capabilities, <see cref="IResourceQueryValidator"/> failures with
/// their paths, and execution of queries that pass validation. It takes the builder's flat condition list, so its
/// paths name the translated query (<c>Filter.Operands[1]</c>); the package's query endpoints take the nested
/// <c>AsterFilter</c> format and report request-body paths.
/// </summary>
internal static class QueryEndpoints
{
    private const int MaxTake = 100;

    public static RouteGroupBuilder MapQueryApi(this RouteGroupBuilder api)
    {
        api.MapGet("/query/capabilities", GetCapabilities)
            .WithName("GetQueryCapabilities");

        api.MapPost("/query/validate", ValidateQuery)
            .WithName("ValidateQuery");

        api.MapPost("/query", RunQueryAsync)
            .WithName("RunQuery");

        return api;
    }

    // The declaration the validator checks queries against.
    private static IResult GetCapabilities(IResourceQueryValidator validator) =>
        validator.Capabilities is { } capabilities ? Results.Ok(capabilities) : Results.NotFound();

    private static IResult ValidateQuery(QueryRequestDto request, HttpContext http, IResourceQueryValidator validator)
    {
        if (!TryTranslate(request, http.GetTenantScope(), out var query, out var error))
            return error;

        var validation = validator.Validate(query);
        return Results.Ok(new QueryValidationDto(validation.ToSummary(), validation.Failures));
    }

    private static async Task<IResult> RunQueryAsync(
        QueryRequestDto request,
        HttpContext http,
        IResourceQueryValidator validator,
        IResourceQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (!TryTranslate(request, http.GetTenantScope(), out var query, out var error))
            return error;

        var validation = validator.Validate(query);
        if (!validation.IsValid)
            return Problem("The query is not valid for this provider.", validation.Failures);

        try
        {
            // One extra row tells whether another page exists.
            var take = query.Take!.Value;
            var rows = (await queryService.QueryAsync(query with { Take = take + 1 }, cancellationToken)).ToList();
            return Results.Ok(new QueryResultDto(
                rows.Take(take).Select(static resource => new QueryResultItemDto(
                    resource.ResourceId,
                    resource.DefinitionId,
                    resource.Version,
                    resource.Created,
                    EndpointMappings.GetTitle(resource))).ToList(),
                rows.Count > take));
        }
        catch (UnsupportedQueryFeatureException exception)
        {
            return Problem("The provider rejected the query.", [new QueryValidationFailure(exception.Code, exception.Message, exception.Path, exception.Feature)]);
        }
    }

    private static bool TryTranslate(QueryRequestDto request, TenantScope tenant, out ResourceQuery query, out IResult error)
    {
        query = null!;
        error = null!;
        if (!EndpointMappings.TryParseLifecycle(request.Lifecycle, out var lifecycle))
        {
            error = Results.BadRequest(new ErrorEnvelope($"Unknown lifecycle '{request.Lifecycle}'. Use all, active, archived or soft-deleted."));
            return false;
        }

        var operands = new List<FilterExpression>();
        foreach (var condition in request.Conditions ?? [])
        {
            if (ToFilter(condition) is not { } filter)
            {
                error = Results.BadRequest(new ErrorEnvelope($"Condition {operands.Count + 1} is incomplete."));
                return false;
            }

            operands.Add(filter);
        }

        query = new ResourceQuery
        {
            TenantScope = tenant,
            DefinitionId = string.IsNullOrWhiteSpace(request.DefinitionId) ? null : request.DefinitionId,
            Scope = request.Scope,
            ActivationChannel = string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel,
            LifecycleState = lifecycle,
            Filter = operands.Count == 0 ? null : new LogicalExpression(request.Match, operands),
            Sorts = (request.Sort ?? [])
                .Select(static sort => new SortExpression(
                    sort.Field,
                    sort.Descending ? SortDirection.Descending : SortDirection.Ascending,
                    string.IsNullOrWhiteSpace(sort.AspectKey) ? null : sort.AspectKey))
                .ToList(),
            Take = Math.Clamp(request.Take ?? 20, 1, MaxTake),
        };
        return true;
    }

    private static FilterExpression? ToFilter(QueryConditionDto condition) => condition switch
    {
        { Kind: "metadata", Field: { Length: > 0 } field } => new MetadataFilter(field, ToValue(condition), condition.Operator),
        { Kind: "facet", AspectKey: { Length: > 0 } aspectKey, FacetId: { Length: > 0 } facetId } => new FacetValueFilter(aspectKey, facetId, ToValue(condition), condition.Operator),
        { Kind: "exists", AspectKey: { Length: > 0 } aspectKey } => new AspectPresenceFilter(aspectKey),
        { Kind: "references", AspectKey: { Length: > 0 } aspectKey, FacetId: { Length: > 0 } facetId, Value: { ValueKind: JsonValueKind.String } target }
            when target.GetString() is { Length: > 0 } targetId => ReferenceQuery.Targets(aspectKey, facetId, targetId),
        _ => null,
    };

    // Values keep their JSON type, so a number stays numeric for range and equality checks; an object is a range.
    private static object ToValue(QueryConditionDto condition) => condition.Value switch
    {
        { ValueKind: JsonValueKind.Object } range => new RangeValue(Bound(range, "min"), Bound(range, "max")),
        { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value => DemoJsonValues.ToPlain(value)!,
        _ => condition.Operator == ComparisonOperator.Exists ? true : string.Empty,
    };

    private static object? Bound(JsonElement range, string name) =>
        range.TryGetProperty(name, out var bound) ? DemoJsonValues.ToPlain(bound) : null;

    private static IResult Problem(string title, IReadOnlyList<QueryValidationFailure> failures) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: title,
            extensions: new Dictionary<string, object?> { ["diagnostics"] = failures });
}
