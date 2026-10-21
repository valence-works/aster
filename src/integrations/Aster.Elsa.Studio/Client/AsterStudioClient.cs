using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aster.Elsa.Studio.Client;

/// <summary>
/// The module's HTTP client over the <c>Aster.AspNetCore</c> endpoints (#108). The <see cref="HttpClient"/>'s base
/// address is the prefix the host passed to <c>MapAster</c>, such as <c>https://server/api/aster/</c>. Every error
/// status becomes an <see cref="AsterApiException"/> carrying the ProblemDetails, including its <c>diagnostics</c>.
/// </summary>
internal sealed class AsterStudioClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private AsterQueryCapabilities? capabilities;
    private bool capabilitiesLoaded;

    /// <summary>Lists the latest version of each definition (<c>GET /definitions</c>).</summary>
    public async Task<IReadOnlyList<AsterDefinitionSummary>> ListDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var array = await GetNodeAsync("definitions", cancellationToken) as JsonArray ?? [];
        return array.OfType<JsonObject>().Select(ReadDefinition).ToList();
    }

    /// <summary>Exports a definition as JSON Schema (<c>GET /definitions/{id}/schema</c>).</summary>
    public async Task<JsonObject> GetDefinitionSchemaAsync(string definitionId, int? version = null, CancellationToken cancellationToken = default)
    {
        var uri = $"definitions/{Escape(definitionId)}/schema{(version is { } v ? $"?version={v}" : "")}";
        return await GetNodeAsync(uri, cancellationToken) as JsonObject
            ?? throw new AsterApiException(new AsterProblem(200, "The schema is not a JSON object.", null, null, []));
    }

    /// <summary>Runs a query (<c>POST /resources/query</c>).</summary>
    public async Task<AsterResourcePageResult> QueryResourcesAsync(JsonObject query, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("resources/query", query, JsonOptions, cancellationToken);
        var page = await ReadAsync(response, cancellationToken) as JsonObject ?? [];
        var items = (page["items"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadResource).ToList();
        return new AsterResourcePageResult(items, ReadBool(page["hasMore"]));
    }

    /// <summary>
    /// Checks a query without running it (<c>POST /resources/query/validate</c>, #194). Every diagnostic carries the
    /// request-body path it concerns.
    /// </summary>
    public async Task<AsterQueryValidationResult> ValidateQueryAsync(JsonObject query, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("resources/query/validate", query, JsonOptions, cancellationToken);
        var result = await ReadAsync(response, cancellationToken) as JsonObject ?? [];
        return new AsterQueryValidationResult(
            ReadBool(result["isValid"]),
            ReadDiagnostics(result["diagnostics"]));
    }

    /// <summary>
    /// Reads the active provider's query capabilities (<c>GET /resources/query/capabilities</c>, #194) once per
    /// client; <see langword="null"/> when the provider declares none.
    /// </summary>
    public async Task<AsterQueryCapabilities?> GetQueryCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (capabilitiesLoaded)
            return capabilities;

        using var response = await http.GetAsync("resources/query/capabilities", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            capabilitiesLoaded = true;
            return capabilities = null;
        }

        var node = await ReadAsync(response, cancellationToken) as JsonObject ?? [];
        var metadataOperators = new HashSet<string>(StringComparer.Ordinal);
        if (node["supportedComparisonOperators"] is JsonObject byFilterType)
        {
            foreach (var (filterType, operators) in byFilterType)
            {
                if (Normalize(filterType) == "metadata" && operators is JsonArray list)
                    metadataOperators.UnionWith(list.Select(static op => Normalize(op?.ToString())));
            }
        }

        var containsFields = (node["metadataContainsFields"] as JsonArray ?? [])
            .Select(static field => Normalize(field?.ToString()))
            .ToHashSet(StringComparer.Ordinal);

        capabilities = new AsterQueryCapabilities(metadataOperators, containsFields, ReadBool(node["supportsMetadataSorting"]));
        capabilitiesLoaded = true;
        return capabilities;
    }

    /// <summary>Gets a resource version (<c>GET /resources/{id}</c>); <see langword="null"/> when it doesn't exist.</summary>
    public async Task<AsterResourceDocument?> GetResourceAsync(string resourceId, int? version = null, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"resources/{Escape(resourceId)}{(version is { } v ? $"?version={v}" : "")}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        return ReadResource(await ReadAsync(response, cancellationToken) as JsonObject ?? []);
    }

    /// <summary>Gets a resource's version history (<c>GET /resources/{id}/versions</c>), newest first.</summary>
    public async Task<IReadOnlyList<AsterVersionSummary>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var history = await GetNodeAsync($"resources/{Escape(resourceId)}/versions", cancellationToken) as JsonObject ?? [];
        return (history["versions"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(static version => new AsterVersionSummary(
                (ReadInt(version["version"]) ?? 0),
                ReadDate(version["created"]),
                ReadBool(version["isLatest"]),
                ReadBool(version["isDraft"]),
                (version["activeChannels"] as JsonArray ?? []).Select(static channel => channel?.ToString() ?? "").ToList(),
                version["lifecycleState"]?.ToString() ?? "none"))
            .OrderByDescending(static version => version.Version)
            .ToList();
    }

    /// <summary>Creates version 1 of a resource (<c>POST /resources</c>).</summary>
    public async Task<AsterResourceDocument> CreateResourceAsync(string definitionId, string? resourceId, JsonObject aspects, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["definitionId"] = definitionId,
            ["resourceId"] = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId,
            ["aspects"] = aspects.DeepClone(),
        };
        using var response = await http.PostAsJsonAsync("resources", body, JsonOptions, cancellationToken);
        return ReadResource(await ReadAsync(response, cancellationToken) as JsonObject ?? []);
    }

    /// <summary>Saves a new version on top of <paramref name="baseVersion"/> (<c>PUT /resources/{id}</c>).</summary>
    public async Task<AsterResourceDocument> UpdateResourceAsync(string resourceId, int baseVersion, JsonObject aspects, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["baseVersion"] = baseVersion, ["aspects"] = aspects.DeepClone() };
        using var response = await http.PutAsJsonAsync($"resources/{Escape(resourceId)}", body, JsonOptions, cancellationToken);
        return ReadResource(await ReadAsync(response, cancellationToken) as JsonObject ?? []);
    }

    /// <summary>Activates a version in a channel (<c>POST /resources/{id}/activate</c>).</summary>
    public Task ActivateAsync(string resourceId, int version, string channel, bool allowMultipleActive, CancellationToken cancellationToken = default) =>
        PostActivationAsync($"resources/{Escape(resourceId)}/activate", version, channel, allowMultipleActive, cancellationToken);

    /// <summary>Deactivates a version in a channel (<c>POST /resources/{id}/deactivate</c>).</summary>
    public Task DeactivateAsync(string resourceId, int version, string channel, CancellationToken cancellationToken = default) =>
        PostActivationAsync($"resources/{Escape(resourceId)}/deactivate", version, channel, allowMultipleActive: false, cancellationToken);

    private async Task PostActivationAsync(string uri, int version, string channel, bool allowMultipleActive, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["version"] = version, ["channel"] = channel, ["allowMultipleActive"] = allowMultipleActive };
        using var response = await http.PostAsJsonAsync(uri, body, JsonOptions, cancellationToken);
        await ReadAsync(response, cancellationToken);
    }

    private async Task<JsonNode?> GetNodeAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<JsonNode?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = response.Content is null ? "" : await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? node = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            {
                // A non-JSON error body (a proxy's HTML page, say) still becomes an AsterApiException below.
            }
        }

        if (response.IsSuccessStatusCode)
            return node;

        var problem = node as JsonObject ?? [];
        throw new AsterApiException(new AsterProblem(
            (int)response.StatusCode,
            problem["title"]?.ToString() ?? response.ReasonPhrase,
            problem["detail"]?.ToString(),
            problem["code"]?.ToString(),
            ReadDiagnostics(problem["diagnostics"])));
    }

    private static IReadOnlyList<AsterDiagnostic> ReadDiagnostics(JsonNode? node) =>
        (node as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(static diagnostic => new AsterDiagnostic(
                diagnostic["code"]?.ToString() ?? "unknown",
                diagnostic["message"]?.ToString() ?? "",
                diagnostic["aspectKey"]?.ToString(),
                diagnostic["facetId"]?.ToString(),
                ReadInt(diagnostic["elementIndex"]),
                diagnostic["path"]?.ToString(),
                diagnostic["feature"]?.ToString()))
            .ToList();

    private static AsterDefinitionSummary ReadDefinition(JsonObject definition) => new(
        definition["definitionId"]?.ToString() ?? "",
        (ReadInt(definition["version"]) ?? 0),
        (definition["aspectDefinitions"] as JsonObject)?.Select(static pair => pair.Key).Order(StringComparer.Ordinal).ToList() ?? [],
        ReadBool(definition["isSingleton"]));

    internal static AsterResourceDocument ReadResource(JsonObject resource) => new(
        resource["resourceId"]?.ToString() ?? "",
        resource["definitionId"]?.ToString() ?? "",
        ReadInt(resource["definitionVersion"]),
        (ReadInt(resource["version"]) ?? 0),
        ReadDate(resource["created"]),
        resource["owner"]?.ToString(),
        resource["aspects"]?.DeepClone() as JsonObject ?? []);

    private static DateTime ReadDate(JsonNode? node) =>
        node is not null && DateTime.TryParse(node.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : default;

    private static bool ReadBool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static int? ReadInt(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Normalizes an enum name from any naming policy: <c>StartsWith</c>, <c>startsWith</c> and <c>starts-with</c> all become <c>startswith</c>.</summary>
    internal static string Normalize(string? name) =>
        string.Concat((name ?? "").Where(static c => c is not ('-' or '_' or ' '))).ToLowerInvariant();
}
