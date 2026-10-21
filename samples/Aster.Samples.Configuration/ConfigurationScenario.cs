using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Samples.Configuration.App;
using Aster.Samples.Configuration.Model;
using Aster.Samples.Configuration.Promotion;

namespace Aster.Samples.Configuration;

/// <summary>
/// The walkthrough <c>dotnet run</c> prints: author configuration, promote it from dev to prod,
/// watch running applications reload, then roll production back.
/// </summary>
public static class ConfigurationScenario
{
    /// <summary>The flag the walkthrough rolls out.</summary>
    public const string NewCheckoutFlag = "new-checkout";

    /// <summary>Version 1 of the flag: on for 10% of the <c>acme</c> tenant.</summary>
    public static FeatureFlag NewCheckoutV1 { get; } = new()
    {
        Key = NewCheckoutFlag,
        Description = "Serve the redesigned checkout.",
        Enabled = true,
        RolloutPercentage = 10,
        AllowedTenants = ["acme"],
    };

    /// <summary>Version 2 of the flag: on for half of <c>acme</c> and <c>globex</c>.</summary>
    public static FeatureFlag NewCheckoutV2 { get; } = NewCheckoutV1 with { RolloutPercentage = 50, AllowedTenants = ["acme", "globex"] };

    /// <summary>Version 1 of the checkout settings.</summary>
    public static AppSettings CheckoutV1() => new()
    {
        Section = CheckoutOptions.Section,
        Values = new JsonObject
        {
            ["PaymentProvider"] = "legacy-gateway",
            ["Retry"] = new JsonObject { ["MaxAttempts"] = 3 },
            ["Currencies"] = new JsonArray("EUR", "USD"),
        },
    };

    /// <summary>Version 2 of the checkout settings: a new payment provider.</summary>
    public static AppSettings CheckoutV2() => new()
    {
        Section = CheckoutOptions.Section,
        Values = new JsonObject
        {
            ["PaymentProvider"] = "stripe",
            ["Retry"] = new JsonObject { ["MaxAttempts"] = 5 },
            ["Currencies"] = new JsonArray("EUR", "USD", "GBP"),
        },
    };

    /// <summary>Runs the walkthrough against <paramref name="asterServices"/> and writes it to <paramref name="output"/>.</summary>
    public static async Task RunAsync(IServiceProvider asterServices, TextWriter output, CancellationToken cancellationToken = default)
    {
        await ConfigurationDefinitions.RegisterAsync(asterServices.GetRequiredService<IResourceDefinitionStore>(), cancellationToken);
        var releases = new ConfigurationReleases(asterServices.GetRequiredService<IResourceManager>());
        var flagId = ConfigurationDefinitions.FeatureFlagResourceId(NewCheckoutFlag);
        var checkoutId = ConfigurationDefinitions.AppSettingsResourceId(CheckoutOptions.Section);

        // One running application per environment, each reading its own channel.
        await using var staging = EnvironmentApplication.Start(asterServices, Environments.Staging);
        await using var prod = EnvironmentApplication.Start(asterServices, Environments.Prod);
        foreach (var app in new[] { staging, prod })
        {
            // Raised by the reload hook during ActivateAsync, once per activation in the app's channel.
            var name = app.Environment;
            app.Checkout.OnChange(_ => output.WriteLine($"    [{name}] configuration reloaded"));
        }

        output.WriteLine("1. Save version 1 and release it to dev. Nothing is active in staging or prod yet.");
        await releases.SaveAsync(NewCheckoutV1, cancellationToken);
        await releases.SaveAsync(CheckoutV1(), cancellationToken);
        await releases.ReleaseAsync(flagId, 1, Environments.Dev, cancellationToken);
        await releases.ReleaseAsync(checkoutId, 1, Environments.Dev, cancellationToken);
        Describe(output, staging, prod);

        output.WriteLine("2. Promote version 1 from dev to staging, then from staging to prod.");
        foreach (var from in new[] { Environments.Dev, Environments.Staging })
        {
            await releases.PromoteAsync(flagId, from, cancellationToken);
            await releases.PromoteAsync(checkoutId, from, cancellationToken);
        }

        Describe(output, staging, prod);

        output.WriteLine("3. Save version 2 (50% rollout, adds globex; stripe) and promote it to staging only.");
        await releases.SaveAsync(NewCheckoutV2, cancellationToken);
        await releases.SaveAsync(CheckoutV2(), cancellationToken);
        await releases.ReleaseAsync(flagId, 2, Environments.Dev, cancellationToken);
        await releases.ReleaseAsync(checkoutId, 2, Environments.Dev, cancellationToken);
        await releases.PromoteAsync(flagId, Environments.Dev, cancellationToken);
        await releases.PromoteAsync(checkoutId, Environments.Dev, cancellationToken);
        Describe(output, staging, prod);

        output.WriteLine("4. Promote version 2 to prod.");
        await releases.PromoteAsync(flagId, Environments.Staging, cancellationToken);
        await releases.PromoteAsync(checkoutId, Environments.Staging, cancellationToken);
        Describe(output, staging, prod);

        output.WriteLine("5. Payments fail in prod: roll the Checkout settings in prod back to version 1. The flag stays on version 2.");
        await releases.RollbackAsync(checkoutId, Environments.Prod, 1, cancellationToken);
        Describe(output, staging, prod);

        output.WriteLine("Version history (nothing was rewritten; rollback only moved the prod activation):");
        var history = asterServices.GetRequiredService<IResourceVersionHistoryService>();
        foreach (var resourceId in new[] { flagId, checkoutId })
        {
            var result = await history.GetHistoryAsync(new ResourceVersionHistoryRequest { ResourceId = resourceId }, cancellationToken);
            foreach (var version in result.Versions)
            {
                var channels = version.ActiveChannels.Count == 0 ? "(not active)" : string.Join(", ", version.ActiveChannels);
                output.WriteLine($"    {resourceId} v{version.Version}: {channels}");
            }
        }
    }

    private static void Describe(TextWriter output, params EnvironmentApplication[] apps)
    {
        foreach (var app in apps)
        {
            var checkout = app.Checkout.CurrentValue;
            output.WriteLine(
                $"    {app.Environment,-8} {NewCheckoutFlag} on for acme {RolloutShare(app, "acme"),2}%, globex {RolloutShare(app, "globex"),2}% | " +
                $"checkout: {checkout.PaymentProvider}, {checkout.Retry.MaxAttempts} attempt(s), [{string.Join(", ", checkout.Currencies)}]");
        }

        output.WriteLine();
    }

    /// <summary>The percentage of 1,000 users of <paramref name="tenant"/> that see the new checkout.</summary>
    public static int RolloutShare(EnvironmentApplication app, string tenant) =>
        Enumerable.Range(0, 1000).Count(user => app.Flags.IsEnabled(NewCheckoutFlag, tenant, $"user-{user}")) / 10;
}
