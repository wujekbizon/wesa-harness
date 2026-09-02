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
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WesaLocalCapability";
    public const string Header = "X-Wesa-Capability-Token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var gregToken = configuration["Security:CapabilityToken"];
        var mobileToken = configuration["Security:MobileMonitorToken"];
        if (string.IsNullOrWhiteSpace(gregToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Capability token is not configured"));
        }

        var supplied = Request.Headers[Header].ToString();
        var greg = Matches(gregToken, supplied);
        var observer = greg || Matches(mobileToken, supplied);
        if (!observer)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid capability token"));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, greg ? "greg" : "mobile-monitor"),
            new(ClaimTypes.Name, greg ? "Greg" : "Mobile monitor"),
            new(ClaimTypes.Role, "observer"),
        };
        if (greg)
        {
            claims.Add(new Claim(ClaimTypes.Role, "greg"));
        }
        var identity = new ClaimsIdentity(
            claims,
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private static bool Matches(string? expected, string supplied)
        => !string.IsNullOrWhiteSpace(expected)
           && !string.IsNullOrWhiteSpace(supplied)
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(expected),
               Encoding.UTF8.GetBytes(supplied));
}
