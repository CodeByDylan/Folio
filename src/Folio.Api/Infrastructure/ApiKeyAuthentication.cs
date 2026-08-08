using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Folio.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Folio.Api.Infrastructure;

/// <summary>Authenticates the caller permitted to trigger a refresh.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiOptions> apiOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "ApiKey";

    /// <summary>The header carrying the key.</summary>
    public const string HeaderName = "X-Folio-Key";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out StringValues supplied))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(apiOptions.CurrentValue.RefreshKey);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid key."));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "refresh")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
