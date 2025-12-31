using System.IdentityModel.Tokens.Jwt;

namespace TILSOFTAI.Api.Security;

public static class JwtRoleExtractor
{
    public static List<string> TryExtractRoles(string jwt)
    {
        var roles = new List<string>();
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt);

            // Common claim names: "roles", "role", "groups"
            foreach (var c in token.Claims)
            {
                if (string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Type, "groups", StringComparison.OrdinalIgnoreCase))
                {
                    // roles có thể là CSV hoặc 1 role/claim
                    foreach (var r in c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        roles.Add(r);
                }
            }
        }
        catch
        {
            // ignore: return empty
        }

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
