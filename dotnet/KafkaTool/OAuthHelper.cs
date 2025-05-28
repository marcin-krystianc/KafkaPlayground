using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using Confluent.Kafka;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public static class OAuthHelper
{
    public static async void OAuthTokenRefreshHandler(IClient client, string cfg, ILogger logger)
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

        logger.LogInformation("Got a new token, Subject:{0}, tokenDater:{1}", subject, tokenDate);
        
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
}