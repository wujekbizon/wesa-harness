using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Wesa.Harness.Api.Infrastructure.Authentication;

public sealed class LocalCapabilityAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CapabilityTokenStore tokenStore)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WesaLocalCapability";
    public const string Header = "X-Wesa-Capability-Token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var supplied = Request.Headers[Header].ToString();
        var authenticated = tokenStore.Authenticate(supplied);
        if (authenticated is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid capability token"));
        }
        var greg = authenticated.Value.Role == "greg";
        var lumio = authenticated.Value.Role == "lumio-client";
        var observer = greg || authenticated.Value.Role == "observer";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, greg ? "greg" : lumio ? "lumio-service" : "mobile-monitor"),
            new(ClaimTypes.Name, greg ? "Greg" : lumio ? "Lumio service" : "Mobile monitor"),
        };
        if (observer)
        {
            claims.Add(new Claim(ClaimTypes.Role, "observer"));
        }
        if (lumio)
        {
            claims.Add(new Claim(ClaimTypes.Role, "lumio-client"));
        }
        if (greg)
        {
            claims.Add(new Claim(ClaimTypes.Role, "greg"));
        }
        var identity = new ClaimsIdentity(
            claims,
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

}
