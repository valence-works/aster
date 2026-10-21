using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

/// <summary>
/// Facet IDs that differ only in the case of their first letter (#240): a facet value is read by its ID, or by the
/// camel-case form when the payload has no such key, so <c>Name</c> and <c>name</c> in one aspect would read each other's
/// value. Every provider refuses such aspects at registration with the same diagnostic, and definitions stored before the
/// rule existed keep loading.
/// </summary>
public abstract class FacetIdCaseCollisionStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task RegisterDefinition_InlineAspectWithFacetIdsDifferingInFirstLetterCase_FailsOnEveryRegistrationPath()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Person")
            .WithAspect("Profile")
            .WithFacet("Name", "string")
            .WithFacet("Title", "string")
            .WithFacet("name", "string")
            .Build();

        var register = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await store.RegisterDefinitionAsync(definition, TenantScope.Default, CancellationToken.None));
        var registerIfLatest = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await store.RegisterDefinitionIfLatestAsync(definition, 0, TenantScope.Default, CancellationToken.None));

        foreach (var exception in new[] { register, registerIfLatest })
        {
            Assert.Equal(
                [
                    (FacetMetadataDiagnosticCodes.IdCaseCollision, "Profile", "Name"),
                    (FacetMetadataDiagnosticCodes.IdCaseCollision, "Profile", "name"),
                ],
                exception.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
            Assert.Contains("'name'", exception.Diagnostics[0].Message, StringComparison.Ordinal);
            Assert.Contains("'Name'", exception.Diagnostics[1].Message, StringComparison.Ordinal);
        }

        Assert.Empty(await store.ListDefinitionsAsync(TenantScope.Default, CancellationToken.None));
    }

    [SkippableFact]
    public async Task RegisterAspect_SharedAspectWithFacetIdsDifferingInFirstLetterCase_FailsOnEveryRegistrationPath()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        var aspect = new AspectDefinition
        {
            AspectDefinitionId = "Contact",
            Id = "contact-1",
            Version = 1,
            FacetDefinitions = [Facet("Email"), Facet("email"), Facet("Phone")],
        };

        var register = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () => await store.RegisterAsync(aspect));
        var registerIfLatest = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () => await store.RegisterIfLatestAsync(aspect, 0));

        foreach (var exception in new[] { register, registerIfLatest })
        {
            Assert.Equal(
                [
                    (FacetMetadataDiagnosticCodes.IdCaseCollision, "Contact", "Email"),
                    (FacetMetadataDiagnosticCodes.IdCaseCollision, "Contact", "email"),
                ],
                exception.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId)));
        }

        Assert.Empty(await store.ListAsync());
    }

    [SkippableFact]
    public async Task RegisterDefinition_TypedAspectWithPropertiesDifferingInFirstLetterCase_Fails()
    {
        await using var provider = Database.CreateProvider();
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Person")
            .WithAspect<CollidingAspect>()
            .Build();

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await store.RegisterDefinitionAsync(definition));

        Assert.Equal(
            ["Name", "name"],
            exception.Diagnostics
                .Where(static diagnostic => diagnostic.Code == FacetMetadataDiagnosticCodes.IdCaseCollision)
                .Select(static diagnostic => diagnostic.FacetDefinitionId));
    }

    [SkippableFact]
    public async Task Register_FacetIdsThatDoNotCollideUnderCamelCase_Succeed()
    {
        await using var provider = Database.CreateProvider();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var aspects = provider.GetRequiredService<IAspectDefinitionStore>();

        // Name and Title differ; Name and NAME camel-case to different keys (name, nAME); Name and name in different
        // aspects are read from different aspect payloads.
        await definitions.RegisterDefinitionAsync(new ResourceDefinitionBuilder()
            .WithDefinitionId("Person")
            .WithAspect("Profile")
            .WithFacet("Name", "string")
            .WithFacet("Title", "string")
            .WithFacet("NAME", "string")
            .WithAspect("Nickname")
            .WithFacet("name", "string")
            .Build());
        await aspects.RegisterAsync(new AspectDefinition
        {
            AspectDefinitionId = "Contact",
            Id = "contact-1",
            Version = 1,
            FacetDefinitions = [Facet("Email"), Facet("Phone")],
        });

        Assert.NotNull(await definitions.GetDefinitionAsync("Person"));
        Assert.NotNull(await aspects.GetLatestAsync("Contact"));
    }

    [SkippableFact]
    public async Task ExistingDefinitionsWithCollidingFacetIds_StillLoad_AndTheirResourcesStayReadableAndUpdatable()
    {
        await using var provider = Database.CreateProvider();

        // Written through the provider's import path, below registration, the way data stored before the rule looks.
        await provider.GetRequiredService<IResourcePortabilityStore>().ApplyImportAsync(new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            AspectDefinitions =
            [
                new AspectDefinition
                {
                    AspectDefinitionId = "Contact",
                    Id = "contact-1",
                    Version = 1,
                    FacetDefinitions = [Facet("Email"), Facet("email")],
                },
            ],
            Definitions =
            [
                new ResourceDefinition
                {
                    DefinitionId = "LegacyPerson",
                    Id = "legacy-person-1",
                    Version = 1,
                    AspectDefinitions = new Dictionary<string, AspectDefinition>
                    {
                        ["Profile"] = new()
                        {
                            AspectDefinitionId = "Profile",
                            Id = "profile-1",
                            Version = 1,
                            FacetDefinitions = [Facet("Name"), Facet("name")],
                        },
                    },
                },
            ],
            Resources =
            [
                new Resource
                {
                    ResourceId = "person-1",
                    Id = "person-1-v1",
                    DefinitionId = "LegacyPerson",
                    DefinitionVersion = 1,
                    Version = 1,
                    Created = DateTime.UtcNow,
                    Aspects = new Dictionary<string, object>
                    {
                        ["Profile"] = new Dictionary<string, object> { ["Name"] = "Ada", ["name"] = "ada" },
                    },
                },
            ],
        });

        var definition = await provider.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("LegacyPerson");
        Assert.Equal(["Name", "name"], definition!.AspectDefinitions["Profile"].FacetDefinitions.Select(static facet => facet.FacetDefinitionId));

        var aspect = await provider.GetRequiredService<IAspectDefinitionStore>().GetLatestAsync("Contact");
        Assert.Equal(["Email", "email"], aspect!.FacetDefinitions.Select(static facet => facet.FacetDefinitionId));

        var manager = provider.GetRequiredService<IResourceManager>();
        Assert.NotNull(await manager.GetLatestVersionAsync("person-1"));

        var updated = await manager.UpdateAsync("person-1", new UpdateResourceRequest
        {
            BaseVersion = 1,
            AspectUpdates = { ["Profile"] = new Dictionary<string, object> { ["Name"] = "Grace", ["name"] = "grace" } },
        });
        Assert.Equal(2, updated.Version);

        // New versions are registrations, so they are refused until the facets are renamed.
        await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await provider.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(definition with { Version = 0 }));
    }

    private static FacetDefinition Facet(string id) => new()
    {
        FacetDefinitionId = id,
        Id = $"facet-{id}-{(char.IsUpper(id[0]) ? "upper" : "lower")}",
        Version = 1,
        Type = "string",
    };

    private sealed class CollidingAspect
    {
        public string? Name { get; init; }

#pragma warning disable IDE1006 // A property named like this is the point of the test.
        public string? name { get; init; }
#pragma warning restore IDE1006
    }
}

public sealed class InMemoryFacetIdCaseCollisionStoreTests() : FacetIdCaseCollisionStoreTests(new InMemoryCoreTestDatabase());

public sealed class SqliteJsonFacetIdCaseCollisionStoreTests() : FacetIdCaseCollisionStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbFacetIdCaseCollisionStoreTests() : FacetIdCaseCollisionStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonFacetIdCaseCollisionStoreTests() : FacetIdCaseCollisionStoreTests(new SqlServerTestDatabase());

/// <summary>
/// The in-memory provider. Unlike the persistent databases, each provider it creates starts empty.
/// </summary>
file sealed class InMemoryCoreTestDatabase : IAsterTestDatabase
{
    public ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddAsterCore();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
    }
}
