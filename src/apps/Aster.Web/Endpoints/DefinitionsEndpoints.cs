using Aster.Core.Abstractions;

namespace Aster.Web.Endpoints;

/// <summary>
/// Read-only endpoints for inspecting registered resource definitions.
/// </summary>
internal static class DefinitionsEndpoints
{
    /// <summary>
    /// Maps the <c>GET /api/definitions</c> endpoint to the provided route builder.
    /// </summary>
    /// <param name="app">The web application to add routes to.</param>
    /// <returns>The <paramref name="app"/> for chaining.</returns>
    internal static WebApplication MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/definitions", async (IResourceDefinitionStore store, CancellationToken ct) =>
        {
            var definitions = await store.ListDefinitionsAsync(ct);
            return Results.Ok(definitions);
        })
        .WithName("GetDefinitions")
        .WithSummary("Returns the latest version of all registered resource definitions.");

        return app;
    }
}
