using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Aster.AspNetCore;

/// <summary>
/// Recovers an ID from its route value when the client escaped it as one path segment (#239).
/// </summary>
/// <remarks>
/// Servers decode every escape in the path except <c>%2F</c>, so that an escaped <c>/</c> doesn't split the segment.
/// A route value can therefore still hold <c>%2F</c>, either from an escaped <c>/</c> (<c>a%2Fb</c>) or from the text
/// <c>%2F</c> (<c>a%252Fb</c>, which the server decoded once). The raw request target tells the two apart; a server
/// without one (TestServer) reads every <c>%2F</c> as <c>/</c>.
/// </remarks>
internal static partial class AsterRouteIds
{
    /// <summary>Returns the ID the client escaped into the route parameter <paramref name="parameter"/>.</summary>
    /// <param name="http">The request.</param>
    /// <param name="value">The parameter's route value.</param>
    /// <param name="parameter">The parameter's name in the endpoint's route pattern.</param>
    public static string Decode(HttpContext http, string value, string parameter)
    {
        // Without a '%', the server has decoded the segment completely.
        if (!value.Contains('%'))
            return value;

        return FromRawTarget(http, value, parameter) ?? EscapedSlash().Replace(value, "/");
    }

    // The parameter's segment of the raw request target, unescaped once, or null when it can't be located.
    private static string? FromRawTarget(HttpContext http, string value, string parameter)
    {
        var raw = http.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (string.IsNullOrEmpty(raw) || raw[0] != '/' || http.GetEndpoint() is not RouteEndpoint endpoint)
            return null;

        var index = IndexOf(endpoint.RoutePattern, parameter);
        if (index < 0)
            return null;

        // Decoding keeps segment boundaries, so the decoded path and the raw one have the same segments, unless
        // the server normalized dot segments or a rewrite changed the path. The checks below fall back then: the
        // raw segment, decoded as the server does (every escape but %2F), must be the route value.
        var query = raw.IndexOf('?');
        var rawSegments = (query < 0 ? raw : raw[..query]).Split('/');
        var pathBase = http.Request.PathBase.Value ?? "";
        var path = (pathBase + http.Request.Path.Value).Split('/');

        // The split path starts with an empty segment, then the path base's, then the pattern's.
        index += 1 + pathBase.Count(static c => c == '/');
        if (rawSegments.Length != path.Length
            || index >= path.Length
            || path[index] != value
            || Uri.UnescapeDataString(EscapedSlash().Replace(rawSegments[index], "%252$1")) != value)
        {
            return null;
        }

        return Uri.UnescapeDataString(rawSegments[index]);
    }

    private static int IndexOf(RoutePattern pattern, string parameter)
    {
        for (var i = 0; i < pattern.PathSegments.Count; i++)
        {
            if (pattern.PathSegments[i].Parts is [RoutePatternParameterPart part]
                && string.Equals(part.Name, parameter, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex("%2([Ff])")]
    private static partial Regex EscapedSlash();
}
