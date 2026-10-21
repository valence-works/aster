using Aster.AdminDemo;
using Aster.AspNetCore;
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5210");
builder.Services.AddAsterCore();
builder.Services.AddAsterSqliteJson(options =>
{
    var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
    Directory.CreateDirectory(dataDirectory);
    options.ConnectionString = ResolveSqliteConnectionString(
        builder.Configuration["Aster:ConnectionString"],
        builder.Environment.ContentRootPath,
        Path.Combine(dataDirectory, "aster-demo.db"));
    options.InitializeSchema = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/openapi/v1.json", () => Results.Json(OpenApiDocumentFactory.Create()))
        .WithName("GetOpenApiDocument");
}

await DemoSeedData.InitializeAsync(app.Services, app.Logger, app.Lifetime.ApplicationStopping);

// Resource, definition and aspect operations come from the package; Studio adds only its view endpoints (#110).
app.MapAster("/api/aster");
app.MapStudioApi();

var clientDist = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
if (Directory.Exists(clientDist))
{
    var clientFiles = new PhysicalFileProvider(clientDist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = clientFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = clientFiles });
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ErrorEnvelope("Endpoint was not found."));
            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(clientDist, "index.html"));
    });
}

app.Run();

static string ResolveSqliteConnectionString(string? configured, string contentRoot, string defaultDatabasePath)
{
    const string dataSourcePrefix = "Data Source=";
    if (string.IsNullOrWhiteSpace(configured))
        return $"{dataSourcePrefix}{defaultDatabasePath}";

    if (!configured.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
        return configured;

    var dataSource = configured[dataSourcePrefix.Length..];
    if (Path.IsPathRooted(dataSource))
        return configured;

    return $"{dataSourcePrefix}{Path.GetFullPath(Path.Combine(contentRoot, dataSource))}";
}
