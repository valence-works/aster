namespace Aster.AdminDemo;

internal static class OpenApiDocumentFactory
{
    public static object Create() => new
    {
        openapi = "3.0.1",
        info = new { title = "Aster AdminDemo API", version = "v1" },
        servers = new[] { new { url = "http://localhost:5210" } },
        paths = new Dictionary<string, object>
        {
            ["/api/aspects"] = Path(Get: Operation("List shared aspects"), Post: Operation("Create shared aspect")),
            ["/api/aspects/{aspectId}"] = Path(Get: Operation("Get shared aspect"), Put: Operation("Save shared aspect as a new version")),
            ["/api/aspects/{aspectId}/upgrade-preview"] = Path(Get: Operation("Preview content types on older aspect versions")),
            ["/api/aspects/{aspectId}/upgrade"] = Path(Post: Operation("Re-pin selected content types to an aspect version")),
            ["/api/definitions"] = Path(Get: Operation("List definitions"), Post: Operation("Create definition")),
            ["/api/definitions/{definitionId}"] = Path(Get: Operation("Get definition")),
            ["/api/resources"] = Path(Get: Operation("List resources"), Post: Operation("Create resource")),
            ["/api/resources/{resourceId}"] = Path(Get: Operation("Get latest resource version"), Put: Operation("Update resource")),
            ["/api/resources/{resourceId}/versions"] = Path(Get: Operation("List resource versions")),
            ["/api/resources/{resourceId}/versions/{version}"] = Path(Get: Operation("Get resource version")),
            ["/api/resources/{resourceId}/activate"] = Path(Post: Operation("Activate resource version")),
            ["/api/resources/{resourceId}/deactivate"] = Path(Post: Operation("Deactivate resource version")),
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
