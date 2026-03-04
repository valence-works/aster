using Aster.Core.Extensions;
using Aster.Web;
using Aster.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Register Aster Core in-memory services
builder.Services.AddAsterCore();

// Seed demo data on startup
builder.Services.AddHostedService<SeedDataInitializer>();

var app = builder.Build();

// Serve static files from wwwroot/ (index.html workbench UI)
app.UseStaticFiles();

// Map read-only API endpoints
app.MapDefinitionsEndpoints();
app.MapResourcesEndpoints();

app.Run();
