using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Benchmarks;

/// <summary>
/// Portable queries over the latest versions, each returning at most one page of 50 results.
/// </summary>
[BenchmarkCategory("Queries")]
public class QueryBenchmarks : AsterBenchmarkBase
{
    private const int PageSize = 50;

    private IResourceQueryService Queries => Data.Services.GetRequiredService<IResourceQueryService>();

    [Benchmark(Description = "Query: metadata (ResourceId)")]
    public Task<List<Resource>> MetadataAsync() =>
        QueryAsync(new MetadataFilter("ResourceId", BenchmarkData.ArticleId(NextArticle()), ComparisonOperator.Equals));

    [Benchmark(Description = "Query: scalar facet (Equals)")]
    public Task<List<Resource>> ScalarFacetAsync() =>
        QueryAsync(new FacetValueFilter("Content", "Status", "draft", ComparisonOperator.Equals));

    [Benchmark(Description = "Query: scalar facet (Range)")]
    public Task<List<Resource>> RangeAsync() =>
        QueryAsync(new FacetValueFilter("Content", "Priority", new RangeValue(3, 5), ComparisonOperator.Range));

    [Benchmark(Description = "Query: array facet (ArrayContains)")]
    public Task<List<Resource>> ArrayFacetAsync() =>
        QueryAsync(new FacetValueFilter("Content", "Tags", $"tag-{NextArticle() % 50}", ComparisonOperator.ArrayContains));

    [Benchmark(Description = "Reverse reference lookup")]
    public Task<ResourceReferrerPage> ReverseLookupAsync() =>
        Data.Services.GetRequiredService<IResourceReferenceService>()
            .GetReferrersAsync(BenchmarkData.AuthorId(NextArticle() % BenchmarkData.Authors), new ResourceReferrerQuery { Take = PageSize })
            .AsTask();

    private async Task<List<Resource>> QueryAsync(FilterExpression filter) =>
        (await Queries.QueryAsync(new ResourceQuery
        {
            DefinitionId = "Article",
            Filter = filter,
            Take = PageSize,
        })).ToList();
}
