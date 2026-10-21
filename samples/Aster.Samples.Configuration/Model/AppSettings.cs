using System.Text.Json.Nodes;

namespace Aster.Samples.Configuration.Model;

/// <summary>
/// The single aspect of an <c>AppSettings</c> resource: one configuration section whose values are
/// an arbitrary JSON tree, stored in an <c>object</c> facet.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The configuration section the values appear under, such as <c>Checkout</c>.</summary>
    public required string Section { get; init; }

    /// <summary>The section's values; nested objects and arrays become nested configuration keys.</summary>
    public required JsonObject Values { get; init; }
}
