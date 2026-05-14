namespace Aster.Core.Models.Querying;

/// <summary>
/// Selects which resource versions a query should consider before filters are evaluated.
/// </summary>
public enum ResourceVersionScope
{
    /// <summary>Only the latest version of each resource.</summary>
    Latest,

    /// <summary>Every stored version of every resource.</summary>
    AllVersions,

    /// <summary>Versions that are active in <see cref="ResourceQuery.ActivationChannel"/>.</summary>
    Active,

    /// <summary>Versions that are not active in any channel.</summary>
    Draft,
}
