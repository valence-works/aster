using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Aster.AspNetCore;

/// <summary>
/// Endpoint metadata carrying the serializer options of one <c>MapAster</c> call, so results and body reads resolve
/// them from the endpoint rather than from the host's <c>ConfigureHttpJsonOptions</c>.
/// </summary>
/// <param name="SerializerOptions">The read-only options from <see cref="AsterEndpointOptions.SerializerOptions"/>.</param>
internal sealed record AsterJsonMetadata(JsonSerializerOptions SerializerOptions);

/// <summary>
/// Reads request bodies and writes responses with the Aster endpoints' own serializer options (#190).
/// </summary>
internal static class AsterJson
{
    private static readonly JsonSerializerOptions Fallback = MakeReadOnly(AsterEndpointOptions.CreateDefaultSerializerOptions());

    /// <summary>Makes <paramref name="options"/> read-only, filling in the reflection resolver when none is set.</summary>
    public static JsonSerializerOptions MakeReadOnly(JsonSerializerOptions options)
    {
        if (!options.IsReadOnly)
            options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Gets the serializer options of the endpoint handling <paramref name="http"/>.</summary>
    public static JsonSerializerOptions Options(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<AsterJsonMetadata>()?.SerializerOptions ?? Fallback;

    /// <summary>A <c>200</c> JSON response.</summary>
    public static IResult Ok(object? value) => new JsonResult(StatusCodes.Status200OK, value, contentType: null, location: null);

    /// <summary>A <c>201</c> JSON response with a <c>Location</c> header.</summary>
    public static IResult Created(string location, object? value) => new JsonResult(StatusCodes.Status201Created, value, contentType: null, location);

    /// <summary>A <c>200</c> JSON Schema document, as <c>application/schema+json</c>.</summary>
    public static IResult Schema(object? value) => new JsonResult(StatusCodes.Status200OK, value, "application/schema+json", location: null);

    /// <summary>
    /// Reads the JSON request body as <typeparamref name="T"/>. Returns the body, or a problem: <c>415</c> for a
    /// non-JSON content type, <c>400</c> with <c>request-body-required</c> for an empty or <c>null</c> body, and
    /// <c>400</c> with <c>request-body-invalid</c> for a body that does not deserialize.
    /// </summary>
    public static async ValueTask<(T? Body, IResult? Problem)> ReadBodyAsync<T>(HttpContext http)
        where T : class
    {
        var request = http.Request;
        if (!request.HasJsonContentType())
            return (null, AsterProblems.UnsupportedMediaType());
        if (request.ContentLength == 0)
            return (null, BodyRequired());

        T? body;
        try
        {
            body = await request.ReadFromJsonAsync<T>(Options(http), http.RequestAborted);
        }
        catch (JsonException exception)
        {
            return (null, AsterProblems.Validation("request-body-invalid", $"The request body is not valid: {exception.Message}"));
        }

        return body is null ? (null, BodyRequired()) : (body, null);
    }

    private static IResult BodyRequired() => AsterProblems.Validation("request-body-required", "A JSON request body is required.");

    private sealed class JsonResult(int statusCode, object? value, string? contentType, string? location)
        : IResult, IStatusCodeHttpResult, IValueHttpResult
    {
        public int? StatusCode => statusCode;

        public object? Value => value;

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = statusCode;
            if (location is not null)
                response.Headers.Location = location;

            return value is null
                ? Task.CompletedTask
                : response.WriteAsJsonAsync(value, value.GetType(), Options(httpContext), contentType, httpContext.RequestAborted);
        }
    }
}
