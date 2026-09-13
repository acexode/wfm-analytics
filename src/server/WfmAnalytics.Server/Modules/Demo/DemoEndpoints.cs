using System.Security.Claims;
using WfmAnalytics.Server.Bootstrap;
using WfmAnalytics.Server.Identity;

namespace WfmAnalytics.Server.Modules.Demo;

public static class DemoEndpoints
{
    public const string RequiredScope = "analytics:demo:read";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/demo/scoped", (ClaimsPrincipal user) => Results.Ok(new
        {
            principalId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            scopes = user.FindAll(DevelopmentIdentity.ScopeClaimType).Select(claim => claim.Value).ToArray(),
            dataClassification = "synthetic-only"
        })).RequireAuthorization(ServerApplication.DemoReadPolicy);

        return endpoints;
    }
}
