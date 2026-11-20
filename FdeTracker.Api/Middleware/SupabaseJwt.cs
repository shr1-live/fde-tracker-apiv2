using System.Text;
using FdeTracker.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FdeTracker.Api.Middleware;

/// <summary>Verifies the token Supabase issued at sign-in: with the legacy shared secret (HS256) if one is set, otherwise with the project's public keys.</summary>
public static class SupabaseJwt
{
    public static void Configure(JwtBearerOptions o, AppOptions app)
    {
        var issuer = app.SupabaseUrl.TrimEnd('/') + "/auth/v1";
        o.MapInboundClaims = false; // keep claim names as "sub" and "email"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = issuer,
            ValidateAudience = true, ValidAudience = "authenticated",
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            RequireSignedTokens = true, RequireExpirationTime = true,
        };
        if (app.SupabaseJwtSecret is { } secret)
        {
            o.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            o.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
        }
        else
        {
            var jwks = new JwksCache($"{issuer}/.well-known/jwks.json");
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => jwks.Keys(kid);
            o.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256];
        }
        o.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                var sentToken = ctx.Request.Headers.Authorization.Count > 0;
                return ErrorBody.Write(ctx.HttpContext, 401, sentToken ? "Invalid or expired session" : "Sign in required");
            },
        };
    }

    sealed class JwksCache(string url)
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        readonly object _lock = new();
        JsonWebKeySet? _set;
        DateTime _fetched;

        public IEnumerable<SecurityKey> Keys(string? kid)
        {
            lock (_lock)
            {
                var stale = _set is null || DateTime.UtcNow - _fetched > TimeSpan.FromHours(1);
                var unknownKid = _set is not null && kid is not null && !_set.Keys.Any(k => k.Kid == kid) && DateTime.UtcNow - _fetched > TimeSpan.FromMinutes(1);
                if (stale || unknownKid)
                {
                    _set = new JsonWebKeySet(Http.GetStringAsync(url).GetAwaiter().GetResult());
                    _fetched = DateTime.UtcNow;
                }
                return _set!.GetSigningKeys();
            }
        }
    }
}
