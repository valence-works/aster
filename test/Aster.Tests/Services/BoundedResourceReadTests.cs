using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Services;

/// <summary>
/// #154: single-resource manager operations read only that resource's versions, never the whole tenant.
/// </summary>
public sealed class BoundedResourceReadTests
{
    [Fact]
    public async Task SingleResourceOperations_ReadOnlyThatResource()
    {
        var store = new InMemoryResourceStore();
        var reader = new RecordingReader(store);
        var manager = new DefaultResourceManager(
            new InMemoryResourceDefinitionStore(NullLogger<InMemoryResourceDefinitionStore>.Instance),
            reader,
            store,
            new GuidIdentityGenerator(),
            NullLogger<DefaultResourceManager>.Instance);

        await manager.CreateAsync("Article", new CreateResourceRequest { ResourceId = "other" });
        reader.Requests.Clear();

        var created = await manager.CreateAsync("Article", new CreateResourceRequest { ResourceId = "a1" });
        await manager.UpdateAsync("a1", new UpdateResourceRequest { BaseVersion = created.Version });
        await manager.ActivateAsync("a1", 1, "published");
        await manager.GetLatestVersionAsync("a1");
        await manager.GetVersionAsync("a1", 1);
        await manager.GetVersionsAsync("a1");
        await manager.GetActiveVersionsAsync("a1", "published");

        Assert.NotEmpty(reader.Requests);
        Assert.All(reader.Requests, static request =>
            Assert.Equal(new[] { "a1" }, request.ResourceIds));
    }

    private sealed class RecordingReader(IResourceVersionReader inner) : IResourceVersionReader
    {
        public List<ResourceVersionReadRequest> Requests { get; } = [];

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.ReadVersionsAsync(request, cancellationToken);
        }
    }
}
