using System.Runtime.CompilerServices;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Aster.Elsa.Activities;

/// <summary>
/// Runs a portable <see cref="ResourceQuery"/> in the Aster tenant mapped from the current Elsa tenant. Archived and
/// soft-deleted resources are excluded unless <see cref="IncludeArchived"/> is set or the query sets
/// <see cref="ResourceQuery.LifecycleState"/> itself, as the list endpoints of <c>Aster.AspNetCore</c> do. The query is
/// validated against the active provider first; every validation failure is reported in one fault.
/// </summary>
[Activity("Aster", "Aster", "Queries Aster resources. Archived and soft-deleted resources are excluded unless Include Archived is set.", DisplayName = "Query Resources")]
public class QueryResources : CodeActivity<IReadOnlyList<Resource>>
{
    /// <inheritdoc />
    public QueryResources([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line)
    {
    }

    /// <summary>
    /// Gets or sets the query: scope, channel, filter, sorting and paging. Its tenant scope, when set, must be the
    /// mapped one. All latest versions when empty.
    /// </summary>
    [Input(Description = "The portable Aster query (scope, channel, filter, sorting, paging). Empty for all latest versions.")]
    public Input<ResourceQuery?>? Query { get; set; }

    /// <summary>Gets or sets the definition to restrict results to; overrides the query's own when set.</summary>
    [Input(Description = "The definition to restrict results to, such as Product. Overrides the query's definition when set.")]
    public Input<string?>? DefinitionId { get; set; }

    /// <summary>
    /// Gets or sets whether to include archived and soft-deleted resources. Ignored when the query sets
    /// <see cref="ResourceQuery.LifecycleState"/>.
    /// </summary>
    [Input(Description = "Include archived and soft-deleted resources. Ignored when the query sets a lifecycle state.")]
    public Input<bool>? IncludeArchived { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var resources = await AsterFaults.RunAsync(async () =>
        {
            var tenant = context.GetAsterTenantScope();
            var query = context.Get(Query) ?? new ResourceQuery();
            if (query.TenantScope is { } requested && requested != tenant)
                throw new AsterActivityException(
                    TenantScopeException.MismatchCode,
                    $"The query names tenant '{requested.TenantId}', but the workflow runs in Aster tenant '{tenant.TenantId}'.");

            var definitionId = context.Get(DefinitionId);
            query = query with
            {
                TenantScope = tenant,
                DefinitionId = string.IsNullOrWhiteSpace(definitionId) ? query.DefinitionId : definitionId,
                LifecycleState = query.LifecycleState ?? (context.Get(IncludeArchived) ? null : ResourceLifecycleMarkerState.None),
            };

            var validation = context.GetRequiredService<IResourceQueryValidator>().Validate(query);
            if (!validation.IsValid)
                throw new AsterActivityException(
                    validation.Failures.Select(static failure => new AsterActivityDiagnostic(failure.Code, failure.Message, Path: failure.Path, Feature: failure.Feature)).ToList(),
                    "The query is not valid for the active provider.");

            IReadOnlyList<Resource> rows = (await context.GetRequiredService<IResourceQueryService>().QueryAsync(query, context.CancellationToken)).ToList();
            return rows;
        });

        context.Set(Result, resources);
    }
}
