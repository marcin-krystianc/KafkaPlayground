using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Confluent.Kafka;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace KafkaTool;

public static class OAuthHelper
{
    public static void OAuthTokenRefreshHandler(IClient client, string cfg, ILogger logger, ProducerConsumerSettings settings)
    {
        if (settings.UseClientAssertionForOAuthTokenCallback)
            OAuthTokenRefreshClientAssertionHandler(client, cfg, logger).GetAwaiter().GetResult();
        else
        {
            OAuthTokenRefreshClientSecretHandler(client, cfg, logger).GetAwaiter().GetResult();
        }
    }

    private static async Task OAuthTokenRefreshClientSecretHandler(IClient client, string cfg, ILogger logger)
    {
        var tokenEndpoint = "http://keycloak:8080/realms/demo/protocol/openid-connect/token";
        var clientId = "kafka-producer-client";
        var clientSecret = "kafka-producer-client-secret";
        var accessTokenClient = new HttpClient();

        var accessToken = await accessTokenClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
        {
            Address = tokenEndpoint,
            ClientId = clientId,
            ClientSecret = clientSecret,
            GrantType = "client_credentials"
        });

        var tokenTicks = GetTokenExpirationTime(accessToken.AccessToken);
        var subject = GetTokenSubject(accessToken.AccessToken);
        var tokenDate = DateTimeOffset.FromUnixTimeSeconds(tokenTicks);
        var ms = tokenDate.ToUnixTimeMilliseconds();

        logger.LogInformation("ClientSecret: Got a new token, Subject:{0}, tokenDater:{1}", subject, tokenDate);

        client.OAuthBearerSetToken(accessToken.AccessToken, ms, subject);
    }

    private static long GetTokenExpirationTime(string token)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtSecurityToken = handler.ReadJwtToken(token);

        var tokenExp = jwtSecurityToken.Claims.First(claim => claim.Type.Equals("exp", StringComparison.Ordinal)).Value;
        var ticks = long.Parse(tokenExp, CultureInfo.InvariantCulture);
        return ticks;
    }

    private static string GetTokenSubject(string token)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtSecurityToken = handler.ReadJwtToken(token);
        return jwtSecurityToken.Claims.First(claim => claim.Type.Equals("sub", StringComparison.Ordinal)).Value;
    }

    private static async Task OAuthTokenRefreshClientAssertionHandler(IClient client, string cfg, ILogger logger)
    {
        var tokenEndpoint = "http://keycloak:8080/realms/demo/protocol/openid-connect/token";
        var clientId = "kafka-consumer-client-jwt";
        var audience = "https://keycloak:8443/realms/demo";
        var privateKeyPath = "/workspace/tmp/client-private-key.pem";
        var accessTokenClient = new HttpClient();

        // Read the private key
        var privateKeyPem = await File.ReadAllTextAsync(privateKeyPath);
        var privateKey = ParsePrivateKeyFromPem(privateKeyPem);

        // Create JWT client assertion
        var clientAssertion = CreateClientAssertion(clientId, audience, privateKey);

        // Prepare token request
        var tokenRequest = new ClientCredentialsTokenRequest
        {
            Address = tokenEndpoint,
            GrantType = "client_credentials",
            ClientId = clientId,
            ClientCredentialStyle = ClientCredentialStyle.PostBody,
            ClientAssertion = new ClientAssertion
            {
                Type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                Value = clientAssertion,
            }
        };

        // Request token
        var accessToken = await accessTokenClient.RequestClientCredentialsTokenAsync(tokenRequest);
        var tokenTicks = GetTokenExpirationTime(accessToken.AccessToken);
        var subject = GetTokenSubject(accessToken.AccessToken);
        var tokenDate = DateTimeOffset.FromUnixTimeSeconds(tokenTicks);
        var ms = tokenDate.ToUnixTimeMilliseconds();

        logger.LogInformation("ClientAssertion: Got a new token, Subject:{0}, tokenDater:{1}", subject, tokenDate);

        client.OAuthBearerSetToken(accessToken.AccessToken, ms, subject);
    }

    private static RSA ParsePrivateKeyFromPem(string privateKeyPem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return rsa;
    }
    
    private static string CreateClientAssertion(string clientId, string audience, RSA privateKey)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: clientId,
            audience: audience,
            claims: new[]
            {
                new Claim("sub", clientId),
                new Claim("jti", Guid.NewGuid().ToString()),
            },
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(privateKey),
                SecurityAlgorithms.RsaSha256
            )
        );

        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(token);
    }
}