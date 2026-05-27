using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Tests.InMemory;

public sealed class InMemoryResourceStoreTests
{
    [Fact]
    public async Task ReadVersionsAsync_ResourceIdsNormalizationPreservesBoundedIntent()
    {
        var store = new InMemoryResourceStore();
        await store.SaveVersionAsync(CreateResource("product-1"));
        await store.SaveVersionAsync(CreateResource("product-2"));

        var nullBound = (await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = null,
        })).ToList();
        var emptyBound = (await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = [],
        })).ToList();
        var invalidOnly = (await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = ["", " "],
        })).ToList();
        var nonOrdinalComparer = (await store.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            ResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PRODUCT-1" },
        })).ToList();

        Assert.Equal(2, nullBound.Count);
        Assert.Equal(2, emptyBound.Count);
        Assert.Empty(invalidOnly);
        Assert.Empty(nonOrdinalComparer);
    }

    private static Resource CreateResource(string resourceId) =>
        new()
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = 1,
            Created = DateTime.UtcNow,
        };
}
