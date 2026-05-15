using System.Linq.Expressions;
using Aster.Core.Models.Querying;

namespace Aster.Core.Extensions;

/// <summary>
/// Factory methods for building portable query filters from typed aspect members.
/// </summary>
public static class TypedQuery
{
    /// <summary>
    /// Creates an aspect presence filter using the aspect type name as the aspect key.
    /// </summary>
    /// <typeparam name="TAspect">The typed aspect.</typeparam>
    /// <param name="aspectKey">Optional aspect key override.</param>
    /// <returns>An aspect presence filter.</returns>
    public static FilterExpression HasAspect<TAspect>(string? aspectKey = null) =>
        new AspectPresenceFilter(ResolveAspectKey<TAspect>(aspectKey));

    /// <summary>
    /// Starts a typed query helper chain for an aspect.
    /// </summary>
    /// <typeparam name="TAspect">The typed aspect.</typeparam>
    /// <param name="aspectKey">Optional aspect key override.</param>
    /// <returns>A typed aspect query builder.</returns>
    public static TypedAspectQuery<TAspect> For<TAspect>(string? aspectKey = null) =>
        new(ResolveAspectKey<TAspect>(aspectKey));

    /// <summary>
    /// Starts a typed query helper chain for an aspect.
    /// </summary>
    /// <typeparam name="TAspect">The typed aspect.</typeparam>
    /// <param name="options">Per-query mapping overrides.</param>
    /// <returns>A typed aspect query builder.</returns>
    public static TypedAspectQuery<TAspect> For<TAspect>(TypedQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(ResolveAspectKey<TAspect>(options.AspectKey), options.FacetIdentifier);
    }

    internal static string ResolveAspectKey<TAspect>(string? aspectKey) =>
        NonEmpty(aspectKey) ?? typeof(TAspect).Name;

    internal static string ResolveMemberName<TAspect, TValue>(Expression<Func<TAspect, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var expression = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : selector.Body;

        return expression is MemberExpression { Member.Name.Length: > 0 } member
            ? member.Member.Name
            : throw new ArgumentException("Typed query selectors must identify a single readable member.", nameof(selector));
    }

    internal static string ResolveFacetIdentifier<TAspect, TValue>(
        Expression<Func<TAspect, TValue>> selector,
        string? facetIdentifier,
        string? defaultFacetIdentifier)
    {
        var resolved = NonEmpty(facetIdentifier)
            ?? NonEmpty(defaultFacetIdentifier)
            ?? ResolveMemberName(selector);

        return resolved;
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Builder for typed filters targeting a specific aspect.
/// </summary>
/// <typeparam name="TAspect">The typed aspect.</typeparam>
public sealed class TypedAspectQuery<TAspect>
{
    private readonly string aspectKey;
    private readonly string? defaultFacetIdentifier;

    internal TypedAspectQuery(string aspectKey, string? defaultFacetIdentifier = null)
    {
        this.aspectKey = aspectKey;
        this.defaultFacetIdentifier = defaultFacetIdentifier;
    }

    /// <summary>
    /// Creates an aspect presence filter for the current aspect.
    /// </summary>
    /// <returns>An aspect presence filter.</returns>
    public FilterExpression HasAspect() => new AspectPresenceFilter(aspectKey);

    /// <summary>
    /// Selects a facet member for typed comparison helpers.
    /// </summary>
    /// <typeparam name="TValue">The selected member value type.</typeparam>
    /// <param name="selector">Expression selecting a single readable member.</param>
    /// <param name="facetIdentifier">Optional facet identifier override.</param>
    /// <returns>A typed facet query builder.</returns>
    public TypedFacetQuery<TValue> Facet<TValue>(
        Expression<Func<TAspect, TValue>> selector,
        string? facetIdentifier = null)
    {
        var resolvedFacetIdentifier = TypedQuery.ResolveFacetIdentifier(selector, facetIdentifier, defaultFacetIdentifier);
        return new(aspectKey, resolvedFacetIdentifier);
    }
}

/// <summary>
/// Builder for typed filters targeting a specific facet.
/// </summary>
/// <typeparam name="TValue">The facet value type.</typeparam>
public sealed class TypedFacetQuery<TValue>
{
    private readonly string aspectKey;
    private readonly string facetIdentifier;

    internal TypedFacetQuery(string aspectKey, string facetIdentifier)
    {
        this.aspectKey = aspectKey;
        this.facetIdentifier = facetIdentifier;
    }

    /// <summary>
    /// Creates an equality facet filter.
    /// </summary>
    /// <param name="value">The expected value.</param>
    /// <returns>A portable facet value filter.</returns>
    public FilterExpression EqualTo(TValue value) =>
        new FacetValueFilter(aspectKey, facetIdentifier, value!, ComparisonOperator.Equals);

    /// <summary>
    /// Creates a string containment facet filter.
    /// </summary>
    /// <param name="value">The substring to match.</param>
    /// <returns>A portable facet value filter.</returns>
    public FilterExpression Contains(string value) =>
        new FacetValueFilter(aspectKey, facetIdentifier, value, ComparisonOperator.Contains);

    /// <summary>
    /// Creates a range facet filter.
    /// </summary>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="includeMin">Whether the lower bound is inclusive.</param>
    /// <param name="includeMax">Whether the upper bound is inclusive.</param>
    /// <returns>A portable facet value filter.</returns>
    public FilterExpression Range(
        TValue? min = default,
        TValue? max = default,
        bool includeMin = true,
        bool includeMax = true) =>
        new FacetValueFilter(
            aspectKey,
            facetIdentifier,
            new RangeValue(min, max, includeMin, includeMax),
            ComparisonOperator.Range);
}
