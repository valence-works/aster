using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Elsa.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Tests;

/// <summary>
/// The sample workflow of the package README (<c>src/integrations/Aster.Elsa/README.md</c>), kept here so it keeps
/// compiling and running. Update both together.
/// </summary>
public class PublishProduct : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var created = builder.WithVariable<Resource>("Created", default!);
        var updated = builder.WithVariable<Resource>("Updated", default!);

        builder.Root = new Sequence
        {
            Activities =
            {
                new CreateResource
                {
                    DefinitionId = new("Product"),
                    Aspects = new("""{ "Title": { "Title": "Gadget" }, "Price": { "Amount": 10 } }"""),
                    Result = new(created),
                },
                new UpdateResource
                {
                    ResourceId = new(context => created.Get(context)!.ResourceId),
                    BaseVersion = new(context => created.Get(context)!.Version),
                    Aspects = new("""{ "Price": { "Amount": 12 } }"""),
                    Result = new(updated),
                },
                new ActivateResourceVersion
                {
                    ResourceId = new(context => updated.Get(context)!.ResourceId),
                    ResourceVersion = new(context => updated.Get(context)!.Version),
                    Channel = new("Published"),
                },
            },
        };
    }
}

public sealed class PublishProductTests
{
    [Fact]
    public async Task The_readme_sample_creates_updates_and_publishes_a_product()
    {
        await using var host = await ElsaTestHost.StartAsync();

        var result = await host.RunWorkflowAsync<PublishProduct>();

        result.AssertCompleted();
        var published = Assert.Single(await host.Services.GetRequiredService<IResourceQueryService>().QueryAsync(new()
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        }));
        Assert.Equal(2, published.Version);
        Assert.True(published.Aspects.ContainsKey("Title"));
    }
}
