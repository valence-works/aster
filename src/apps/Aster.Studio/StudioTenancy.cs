using Aster.AspNetCore;
using Aster.Core.Models.Tenancy;

namespace Aster.Studio;

/// <summary>
/// Studio's tenants. The SPA names the selected tenant in the <c>X-Aster-Tenant</c> header, and every endpoint,
/// the package's and Studio's own, resolves it through <see cref="ITenantScopeAccessor"/>.
/// </summary>
internal static class StudioTenants
{
    public const string HeaderName = "X-Aster-Tenant";

    public static readonly TenantScope Acme = TenantScope.FromTenantId("acme");

    public static readonly IReadOnlyList<TenantDto> All = [new(TenantScope.DefaultTenantId, "Default"), new(Acme.TenantId, "Acme")];

    public static TenantScope GetTenantScope(this HttpContext http) =>
        http.RequestServices.GetRequiredService<ITenantScopeAccessor>().GetTenantScope(http);

    /// <summary>
    /// Rejects a tenant Studio doesn't seed. Studio has no sign-in to check the header against, so its seeded tenants
    /// are the allow-list; a real host resolves the tenant from the caller's identity instead.
    /// </summary>
    public static void UseStudioTenants(this WebApplication app) => app.Use(async (context, next) =>
    {
        var named = context.Request.Headers[HeaderName].ToString();
        if (named.Length > 0 && !All.Any(tenant => tenant.TenantId == named))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Unknown tenant '{named}'.",
                extensions: new Dictionary<string, object?> { ["diagnostics"] = new[] { new DiagnosticDto("tenant-unknown", null, null, $"Tenant '{named}' does not exist.") } })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    });

    public static RouteGroupBuilder MapTenantApi(this RouteGroupBuilder api)
    {
        api.MapGet("/tenants", () => Results.Ok(All))
            .WithName("GetTenants");

        return api;
    }
}

internal sealed class StudioTenantAccessor : ITenantScopeAccessor
{
    public TenantScope GetTenantScope(HttpContext httpContext) =>
        httpContext.Request.Headers[StudioTenants.HeaderName].ToString() is { Length: > 0 } tenantId
            ? TenantScope.FromTenantId(tenantId)
            : TenantScope.Default;
}
