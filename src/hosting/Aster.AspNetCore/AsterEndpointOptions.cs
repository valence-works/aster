using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Aster.AspNetCore;

/// <summary>
/// The endpoint groups <see cref="AsterEndpointRouteBuilderExtensions.MapAster"/> can map.
/// </summary>
[Flags]
public enum AsterEndpointGroups
{
    /// <summary>No endpoints.</summary>
    None = 0,

    /// <summary>Resource definitions: <c>/definitions</c>, including their JSON Schema.</summary>
    Definitions = 1,

    /// <summary>Shared aspect definitions: <c>/aspects</c>, including their JSON Schema.</summary>
    Aspects = 2,

    /// <summary>Resources: <c>/resources</c> (list, query, detail, versions, create, update, activation).</summary>
    Resources = 4,

    /// <summary>Every group.</summary>
    All = Definitions | Aspects | Resources,
}

/// <summary>
/// Options for <see cref="AsterEndpointRouteBuilderExtensions.MapAster"/>.
/// </summary>
public sealed class AsterEndpointOptions
{
    /// <summary>Gets or sets the groups to map. Defaults to <see cref="AsterEndpointGroups.All"/>.</summary>
    public AsterEndpointGroups Groups { get; set; } = AsterEndpointGroups.All;

    /// <summary>
    /// Gets or sets whether only reading endpoints are mapped: no registration, create, update or activation.
    /// The query endpoint (<c>POST /resources/query</c>) is a read and stays mapped.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets the authorization policy each group requires, by group. A group without an entry inherits whatever the
    /// host applies to the route group <c>MapAster</c> returns.
    /// </summary>
    public IDictionary<AsterEndpointGroups, string> AuthorizationPolicies { get; } = new Dictionary<AsterEndpointGroups, string>();

    /// <summary>
    /// Gets or sets a callback invoked for each mapped group's route group, for host conventions such as rate
    /// limiting, CORS or extra metadata.
    /// </summary>
    public Action<AsterEndpointGroups, RouteGroupBuilder>? ConfigureGroup { get; set; }

    /// <summary>Gets or sets the default page size of <c>GET /resources</c>. Defaults to 50.</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>Gets or sets the largest page size <c>GET /resources</c> and the query endpoint accept. Defaults to 500.</summary>
    public int MaxPageSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the JSON options that read every request body and write every response of the Aster endpoints,
    /// independent of the host's <c>ConfigureHttpJsonOptions</c>, so the wire format does not change with the host's
    /// global JSON settings. Defaults to web defaults (camelCase properties, case-insensitive reads) with enums
    /// written as camelCase names, such as <c>softDeleted</c> or <c>possibleCandidate</c>; enum integers are still
    /// accepted on read. A host may adjust these options or replace them, for example with kebab-case enum names.
    /// <c>MapAster</c> makes them read-only.
    /// </summary>
    public JsonSerializerOptions SerializerOptions
    {
        get => serializerOptions;
        set => serializerOptions = value ?? throw new ArgumentNullException(nameof(value));
    }

    private JsonSerializerOptions serializerOptions = CreateDefaultSerializerOptions();

    internal static JsonSerializerOptions CreateDefaultSerializerOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
