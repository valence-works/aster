using System.Linq.Expressions;
using System.Reflection;
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

    /// <summary>
    /// Combines filter expressions with a logical AND.
    /// </summary>
    /// <param name="operands">The filter expressions to combine.</param>
    /// <returns>A logical AND expression.</returns>
    public static FilterExpression And(params FilterExpression[] operands) =>
        Logical(LogicalOperator.And, operands);

    /// <summary>
    /// Combines filter expressions with a logical OR.
    /// </summary>
    /// <param name="operands">The filter expressions to combine.</param>
    /// <returns>A logical OR expression.</returns>
    public static FilterExpression Or(params FilterExpression[] operands) =>
        Logical(LogicalOperator.Or, operands);

    /// <summary>
    /// Negates a single filter expression.
    /// </summary>
    /// <param name="operands">The single filter expression to negate.</param>
    /// <returns>A logical NOT expression.</returns>
    public static FilterExpression Not(params FilterExpression[] operands)
    {
        if (operands.Length != 1)
            throw new ArgumentException("Typed query Not composition requires exactly one operand.", nameof(operands));

        return Logical(LogicalOperator.Not, operands);
    }

    internal static string ResolveAspectKey<TAspect>(string? aspectKey) =>
        NonEmpty(aspectKey) ?? typeof(TAspect).Name;

    internal static string ResolveMemberName<TAspect, TValue>(Expression<Func<TAspect, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var expression = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : selector.Body;

        if (expression is not MemberExpression { Member.Name.Length: > 0 } member
            || !ReferenceEquals(member.Expression, selector.Parameters[0])
            || !IsReadableMember(member.Member))
        {
            throw new ArgumentException("Typed query selectors must identify a single readable member on the typed aspect.", nameof(selector));
        }

        return member.Member.Name;
    }

    internal static string ResolveFacetIdentifier<TAspect, TValue>(
        Expression<Func<TAspect, TValue>> selector,
        string? facetIdentifier,
        string? defaultFacetIdentifier)
    {
        var memberName = ResolveMemberName(selector);
        var resolved = NonEmpty(facetIdentifier)
            ?? NonEmpty(defaultFacetIdentifier)
            ?? memberName;

        return resolved;
    }

    private static FilterExpression Logical(LogicalOperator logicalOperator, IReadOnlyList<FilterExpression> operands)
    {
        ArgumentNullException.ThrowIfNull(operands);

        if (operands.Count == 0)
            throw new ArgumentException("Typed query logical composition requires at least one operand.", nameof(operands));

        if (operands.Any(operand => operand is null))
            throw new ArgumentException("Typed query logical composition does not accept null operands.", nameof(operands));

        return new LogicalExpression(logicalOperator, operands);
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsReadableMember(MemberInfo member) => member switch
    {
        PropertyInfo { CanRead: true } => true,
        FieldInfo => true,
        _ => false,
    };
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
    /// <remarks>
    /// Containment is a string-oriented predicate. Using this on non-string typed facets produces
    /// a syntactically valid AST node, but execution depends on how the active provider serializes
    /// the facet value. Prefer <see cref="EqualTo"/> or <see cref="Range"/> for strongly typed
    /// non-string comparisons.
    /// </remarks>
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
        object? min = null,
        object? max = null,
        bool includeMin = true,
        bool includeMax = true) =>
        new FacetValueFilter(
            aspectKey,
            facetIdentifier,
            new RangeValue(min, max, includeMin, includeMax),
            ComparisonOperator.Range);

    /// <summary>
    /// Creates an ascending facet sort.
    /// </summary>
    /// <returns>A portable facet sort expression.</returns>
    public SortExpression Ascending() =>
        Sort(SortDirection.Ascending);

    /// <summary>
    /// Creates a descending facet sort.
    /// </summary>
    /// <returns>A portable facet sort expression.</returns>
    public SortExpression Descending() =>
        Sort(SortDirection.Descending);

    /// <summary>
    /// Creates a facet sort with the specified direction.
    /// </summary>
    /// <param name="direction">The sort direction.</param>
    /// <returns>A portable facet sort expression.</returns>
    public SortExpression Sort(SortDirection direction) =>
        new(facetIdentifier, direction, aspectKey);
}
