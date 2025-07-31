// Controllers/OidcController.cs
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;

[ApiController]
[Route("")] // Root route for OIDC endpoints
public class OidcController : ControllerBase
{
    // A very simplistic "client" definition for demonstration.
    // In a real server, this would come from a database.
    private const string MOCK_CLIENT_ID = "test_client";
    private const string MOCK_CLIENT_SECRET = "test_secret"; // Used only for demo, not secure
    private static readonly List<string> MOCK_REDIRECT_URIS = new List<string>
    {
        "https://localhost:5001/signin-oidc", // Example for ASP.NET Core OIDC client
        "http://localhost:5000/signin-oidc",
        "https://oauth.pstmn.io/v1/browser-callback" // For Postman
    };

    // This is a symmetric key for signing JWTs.
    // **DO NOT USE THIS KEY IN PRODUCTION. GENERATE A STRONG, SECURE KEY.**
    private static readonly SymmetricSecurityKey SIGNING_KEY =
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes("thisisverysecretkeyfordemopurposesonly1234567890"));

    private const string ISSUER = "https://localhost:7045"; // Match your server's base URL

    [HttpGet(".well-known/openid-kewy-configuration")] // This is the standard OIDC discovery endpoint
    public IActionResult GetDiscoveryDocument()
    {
        var discoveryDocument = new
        {
            issuer = ISSUER,
            authorization_endpoint = $"{ISSUER}/authorize",
            token_endpoint = $"{ISSUER}/token",
            jwks_uri = $"{ISSUER}/jwks", // Not fully implemented, but good to include
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.HmacSha256 }, // For symmetric key
            scopes_supported = new[] { "openid", "profile", "email" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
            // Add other standard fields as needed for more advanced mock
        };
        return Ok(discoveryDocument);
    }

    [HttpGet("jwks")] // JSON Web Key Set endpoint - very basic mock
    public IActionResult GetJwks()
    {
        // In a real scenario, this would expose your public keys used for signing.
        // For symmetric key, you might not expose it, or just a dummy.
        // For asymmetric, you'd provide the public key.
        // For this mock, we'll return an empty or dummy set as we're using symmetric.
        // A proper JWKS for symmetric key is tricky without exposing the key itself.
        // For HMACSHA256, usually you don't expose the key via JWKS.
        // If you were using RSA, you'd generate and provide the public modulus and exponent.
        var jwks = new
        {
            keys = new[] {
                new {
                    kty = "oct", // Octet sequence (symmetric key)
                    alg = SecurityAlgorithms.HmacSha256,
                    kid = "mock_key_id",
                    // For symmetric, 'k' (key value) field would be the base64url encoded key
                    // but we typically don't expose symmetric keys publicly.
                    // This is just a placeholder for clients that expect a JWKS endpoint.
                }
            }
        };
        return Ok(jwks);
    }


    [HttpGet("authorize")]
    public IActionResult Authorize(
        [FromQuery(Name = "response_type")] string responseType,
        [FromQuery(Name = "client_id")] string clientId,
        [FromQuery(Name = "redirect_uri")] string redirectUri,
        [FromQuery(Name = "scope")] string scope,
        [FromQuery(Name = "state")] string state,
        [FromQuery(Name = "nonce")] string nonce)
    {
        // Basic validation (or lack thereof)
        if (responseType != "code")
        {
            return BadRequest("Unsupported response_type. Only 'code' is supported.");
        }

        if (clientId != MOCK_CLIENT_ID)
        {
            return BadRequest("Invalid client_id.");
        }

        if (!MOCK_REDIRECT_URIS.Contains(redirectUri))
        {
            return BadRequest("Invalid redirect_uri.");
        }

        // Simulate user consent/login (in a real app, this would be a UI)
        // For this mock, we just approve and generate a code.

        // Generate a mock authorization code. In a real system, this would be
        // stored and linked to the user's session and the requested scopes/claims.
        var authorizationCode = Guid.NewGuid().ToString("N");

        // We'll store a very basic representation of the "code" for later use by the token endpoint.
        // In a real app, this would be in a database/cache with expiry.
        // For simplicity, we'll use a static dictionary.
        // **NOT THREAD-SAFE OR PRODUCTION-READY**
        MockAuthorizationCodeStore.Codes[authorizationCode] = new MockCodeInfo
        {
            ClientId = clientId,
            RedirectUri = redirectUri,
            Scope = scope,
            Nonce = nonce
        };

        // Redirect back to the client with the authorization code
        var redirectUrl = $"{redirectUri}?code={authorizationCode}&state={state}";
        Console.WriteLine($"Redirecting to: {redirectUrl}"); // For debugging
        return Redirect(redirectUrl);
    }

    [HttpPost("token")]
    public IActionResult Token(
        [FromForm(Name = "grant_type")] string grantType,
        [FromForm(Name = "code")] string code,
        [FromForm(Name = "redirect_uri")] string redirectUri,
        [FromForm(Name = "client_id")] string clientId,
        [FromForm(Name = "client_secret")] string clientSecret)
    {
        // Basic validation
        if (grantType != "authorization_code")
        {
            return BadRequest("Unsupported grant_type. Only 'authorization_code' is supported.");
        }

        if (clientId != MOCK_CLIENT_ID || clientSecret != MOCK_CLIENT_SECRET)
        {
            return Unauthorized("Invalid client credentials.");
        }

        if (!MockAuthorizationCodeStore.Codes.TryGetValue(code, out var codeInfo))
        {
            return BadRequest("Invalid or expired authorization code.");
        }

        if (codeInfo.ClientId != clientId || codeInfo.RedirectUri != redirectUri)
        {
            return BadRequest("Mismatched code parameters.");
        }

        // Remove the code after use (single-use code)
        MockAuthorizationCodeStore.Codes.Remove(code);

        // Generate ID Token
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, "mock_user_id"), // Subject
            new Claim(JwtRegisteredClaimNames.Name, "Mock User"),
            new Claim(JwtRegisteredClaimNames.Email, "mockuser@example.com"),
            new Claim("preferred_username", "mockuser"),
            new Claim(JwtRegisteredClaimNames.Aud, clientId), // Audience is the client_id
            new Claim(JwtRegisteredClaimNames.Iss, ISSUER),   // Issuer is our server
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString()) // Expires in 5 min
        };

        if (!string.IsNullOrEmpty(codeInfo.Nonce))
        {
            claims.Add(new Claim("nonce", codeInfo.Nonce)); // Include nonce if present
        }

        var idTokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5), // Short-lived for demo
            SigningCredentials = new SigningCredentials(SIGNING_KEY, SecurityAlgorithms.HmacSha256),
            Issuer = ISSUER,
            Audience = clientId
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var idToken = tokenHandler.CreateToken(idTokenDescriptor);
        var signedIdToken = tokenHandler.WriteToken(idToken);

        // Generate Access Token (simpler, usually contains less claims)
        var accessTokenClaims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, "mock_user_id"),
            new Claim("scope", codeInfo.Scope),
            new Claim(JwtRegisteredClaimNames.Aud, clientId),
            new Claim(JwtRegisteredClaimNames.Iss, ISSUER),
            new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString()) // Longer expiry
        };

        var accessTokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(accessTokenClaims),
            Expires = DateTime.UtcNow.AddMinutes(10), // Short-lived for demo
            SigningCredentials = new SigningCredentials(SIGNING_KEY, SecurityAlgorithms.HmacSha256),
            Issuer = ISSUER,
            Audience = clientId
        };
        var accessToken = tokenHandler.CreateToken(accessTokenDescriptor);
        var signedAccessToken = tokenHandler.WriteToken(accessToken);


        // Return the tokens
        return Ok(new
        {
            access_token = signedAccessToken,
            token_type = "Bearer",
            expires_in = 600, // 10 minutes
            id_token = signedIdToken,
            scope = codeInfo.Scope
        });
    }
}

// Simple in-memory store for authorization codes.
// **NOT PRODUCTION-READY: No expiry, no thread safety.**
public static class MockAuthorizationCodeStore
{
    public static Dictionary<string, MockCodeInfo> Codes { get; } = new Dictionary<string, MockCodeInfo>();
}

public class MockCodeInfo
{
    public string ClientId { get; set; }
    public string RedirectUri { get; set; }
    public string Scope { get; set; }
    public string Nonce { get; set; }
}