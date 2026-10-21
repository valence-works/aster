using System.Text.Json;
using System.Text.Json.Serialization;
using Aster.Studio;
using Aster.AspNetCore;
using Aster.Core.Extensions;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5210");
// SDK results carry enums such as lifecycle states and policy outcomes; the SPA reads them as kebab-case names.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)));
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
builder.Services.AddSingleton<ITenantScopeAccessor, StudioTenantAccessor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/openapi/v1.json", () => Results.Json(OpenApiDocumentFactory.Create()))
        .WithName("GetOpenApiDocument");
}

await DemoSeedData.InitializeAsync(app.Services, app.Logger, app.Lifetime.ApplicationStopping);
app.UseStudioTenants();

// Resource and definition operations come from the package; Studio adds only its view endpoints (#110).
// Studio keeps two write rules the package endpoints leave to the host:
// - Shared aspects are written only through Studio's create-only and base-version checks, so the package's
//   unconditional aspect registration is not mapped (#175 adds a conditional one).
// - Content types are create-only: registering a definition must name expectedLatestVersion=0.
app.MapAster("/api/aster", options =>
{
    options.Groups = AsterEndpointGroups.Definitions | AsterEndpointGroups.Resources;
    // The package endpoints have their own JSON options (camelCase enums); keep the SPA's kebab-case enum names there too.
    options.SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };
    options.ConfigureGroup = static (group, builder) =>
    {
        if (group == AsterEndpointGroups.Definitions)
            builder.AddEndpointFilter(RequireCreateOnlyDefinitionRegistration);
    };
});
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

static ValueTask<object?> RequireCreateOnlyDefinitionRegistration(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var request = context.HttpContext.Request;
    if (HttpMethods.IsPost(request.Method) && request.Query["expectedLatestVersion"] != "0")
    {
        return ValueTask.FromResult<object?>(Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Content types are create-only in Studio.",
            detail: "Register a definition with ?expectedLatestVersion=0."));
    }

    return next(context);
}

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
