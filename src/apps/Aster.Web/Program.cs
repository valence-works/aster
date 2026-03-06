using Aster.Core.Extensions;
using Aster.Persistence.Sqlite.Extensions;
using Aster.Web;
using Aster.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Register Aster Core in-memory services
builder.Services.AddAsterCore();

// Register Aster Sqlite persistence provider
builder.Services.AddSqlitePersistence(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Sqlite")
                               ?? "Data Source=aster.db";
});

// Seed demo data on startup
builder.Services.AddHostedService<SeedDataInitializer>();

var app = builder.Build();

// Serve static files from wwwroot/ (index.html workbench UI)
app.UseStaticFiles();

// Map read-only API endpoints
app.MapDefinitionsEndpoints();
app.MapResourcesEndpoints();

app.Run();
