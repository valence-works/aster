using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;

namespace Aster.Web.Endpoints;

/// <summary>
/// Read-only endpoints for inspecting resource instances by definition.
/// </summary>
internal static class ResourcesEndpoints
{
    /// <summary>
    /// Maps the <c>GET /api/resources/{definitionId}</c> endpoint to the provided route builder.
    /// </summary>
    /// <param name="app">The web application to add routes to.</param>
    /// <returns>The <paramref name="app"/> for chaining.</returns>
    internal static WebApplication MapResourcesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/resources/{definitionId}", async (
            string definitionId,
            IResourceQueryService queryService,
            CancellationToken ct) =>
        {
            var resources = await queryService.QueryAsync(
                new ResourceQuery { DefinitionId = definitionId }, ct);
            return Results.Ok(resources);
        })
        .WithName("GetResourcesByDefinition")
        .WithSummary("Returns all latest resource versions for the specified definition.");

        return app;
    }
}
