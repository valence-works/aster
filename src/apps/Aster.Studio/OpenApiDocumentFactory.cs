namespace Aster.Studio;

internal static class OpenApiDocumentFactory
{
    public static object Create() => new
    {
        openapi = "3.0.1",
        info = new { title = "Aster Studio API", version = "v1" },
        servers = new[] { new { url = "http://localhost:5210" } },
        paths = new Dictionary<string, object>
        {
            // Studio's own view endpoints. Resource, definition and aspect operations are the Aster.AspNetCore
            // endpoints under /api/aster (src/hosting/Aster.AspNetCore/README.md).
            ["/api/studio/aspects"] = Path(Get: Operation("List shared aspects with versions and usage"), Post: Operation("Create a shared aspect (409 if it exists)")),
            ["/api/studio/aspects/{aspectId}"] = Path(Get: Operation("Get a shared aspect with versions and usage"), Put: Operation("Save a shared aspect as a new version, based on baseVersion")),
            ["/api/studio/aspects/{aspectId}/upgrade-preview"] = Path(Get: Operation("Preview content types on older aspect versions")),
            ["/api/studio/aspects/{aspectId}/upgrade"] = Path(Post: Operation("Re-pin selected content types to an aspect version")),
            ["/api/studio/definitions"] = Path(Get: Operation("List definitions with resource counts")),
            ["/api/studio/resources"] = Path(Get: Operation("List a definition's resources with title, tags, active channels and lifecycle state; ?lifecycle=all|active|archived|soft-deleted")),
            ["/api/studio/resources/{resourceId}/lifecycle"] = Path(Get: Operation("Get a resource's lifecycle marker"), Post: Operation("Archive or soft-delete a resource")),
            ["/api/studio/resources/{resourceId}/restore-preview"] = Path(Post: Operation("Preview restoring a resource from an expected lifecycle state")),
            ["/api/studio/resources/{resourceId}/restore"] = Path(Post: Operation("Restore a resource from an expected lifecycle state")),
            ["/api/studio/resources/{resourceId}/schema"] = Path(Get: Operation("Get the latest version's schema status and undeclared aspect keys")),
            ["/api/studio/resources/{resourceId}/schema-upgrade"] = Path(Post: Operation("Upgrade the latest version to the latest definition version, based on baseVersion")),
            ["/api/studio/definitions/{definitionId}/policies"] = Path(Get: Operation("List a definition's policy declarations with validation diagnostics")),
            ["/api/studio/definitions/{definitionId}/policies/preview"] = Path(Post: Operation("Preview a definition's policies, with pruning protection per version")),
            ["/api/studio/policies/apply"] = Path(Post: Operation("Apply selected archive and soft-delete preview candidates")),
            ["/api/studio/policies/prune"] = Path(Post: Operation("Prune selected version-pruning preview candidates")),
        },
    };

    private static Dictionary<string, object> Path(
        object? Get = null,
        object? Post = null,
        object? Put = null)
    {
        var path = new Dictionary<string, object>(StringComparer.Ordinal);
        if (Get is not null)
            path["get"] = Get;
        if (Post is not null)
            path["post"] = Post;
        if (Put is not null)
            path["put"] = Put;
        return path;
    }

    private static object Operation(string summary) => new
    {
        summary,
        responses = new Dictionary<string, object>
        {
            ["200"] = new { description = "OK" },
            ["201"] = new { description = "Created" },
            ["400"] = new { description = "Validation error" },
            ["404"] = new { description = "Not found" },
            ["409"] = new { description = "Concurrency conflict" },
        },
    };
}
