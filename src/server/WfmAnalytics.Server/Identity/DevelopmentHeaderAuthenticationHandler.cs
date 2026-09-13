using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WfmAnalytics.Server.Bootstrap;

namespace WfmAnalytics.Server.Identity;

public sealed class DevelopmentHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principalName = Request.Headers[DevelopmentIdentity.PrincipalHeader].ToString().Trim();
        if (string.IsNullOrEmpty(principalName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (principalName is not ("demo-manager" or "demo-unscoped"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid synthetic principal."));
        }

        string[] scopeValues = principalName == "demo-manager" ? ["analytics:demo:read"] : [];

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principalName),
            new(ClaimTypes.Name, principalName)
        };
        claims.AddRange(scopeValues.Select(scope => new Claim(DevelopmentIdentity.ScopeClaimType, scope)));

        var identity = new ClaimsIdentity(claims, ServerApplication.DevelopmentAuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ServerApplication.DevelopmentAuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

}
