using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Instances;

namespace Aster.Web;

/// <summary>
/// Seeds demo data into the Aster Core in-memory store on application startup.
/// Registers a "Product" definition and creates three sample resource instances.
/// </summary>
internal sealed class SeedDataInitializer : BackgroundService
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager resourceManager;
    private readonly ILogger<SeedDataInitializer> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SeedDataInitializer"/>.
    /// </summary>
    public SeedDataInitializer(
        IResourceDefinitionStore definitionStore,
        IResourceManager resourceManager,
        ILogger<SeedDataInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(resourceManager);
        ArgumentNullException.ThrowIfNull(logger);

        this.definitionStore = definitionStore;
        this.resourceManager = resourceManager;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Seeding Aster Core demo data...");

        // Register "Product" definition
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .WithAspect<TitleAspect>()
            .WithAspect<PriceAspect>()
            .Build();

        await definitionStore.RegisterDefinitionAsync(definition, stoppingToken);
        logger.LogInformation("Registered 'Product' definition (version {Version}).", 1);

        // Create sample product resources
        var samples = new[]
        {
            new { Title = "Super Gadget", Price = 99.99m, Currency = "USD" },
            new { Title = "Mega Widget",  Price = 49.50m, Currency = "USD" },
            new { Title = "Micro Donut",  Price = 12.00m, Currency = "EUR" },
        };

        foreach (var sample in samples)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resource = await resourceManager.CreateAsync("Product", new CreateResourceRequest
            {
                InitialAspects = new Dictionary<string, object>
                {
                    ["TitleAspect"] = new TitleAspect(sample.Title),
                    ["PriceAspect"] = new PriceAspect(sample.Price, sample.Currency),
                }
            }, stoppingToken);

            // Activate V1 in Published channel
            await resourceManager.ActivateAsync(resource.ResourceId, 1, "Published", ChannelMode.SingleActive, cancellationToken: stoppingToken);
            logger.LogInformation("Seeded product '{Title}' (ResourceId={ResourceId}).", sample.Title, resource.ResourceId);
        }

        logger.LogInformation("Aster Core demo data seeding complete.");
    }

    // ── Demo POCOs ────────────────────────────────────────────────────────────

    private sealed record TitleAspect(string Title);
    private sealed record PriceAspect(decimal Amount, string Currency);
}
