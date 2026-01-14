using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace AzFilesOptimizer.Backend.Services
{
    /// <summary>
    /// Service for handling authentication and user extraction from HTTP requests
    /// </summary>
    public class AuthenticationService
    {
        private readonly IConfiguration _configuration;

        public AuthenticationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Extracts user information from the HTTP request token
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <returns>Authenticated user information or null if invalid</returns>
        public async Task<AuthenticatedUser?> ExtractUserAsync(HttpRequestData req)
        {
            try
            {
                // Check if authorization header exists
                if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
                {
                    return null;
                }

                var authHeader = authHeaders.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    return null;
                }

                // Get the token without "Bearer " prefix
                string token = authHeader.Substring("Bearer ".Length);

                // Validate and extract claims from token using Microsoft.Identity.Web
                var tokenValidationResult = await ValidateTokenAsync(token);
                
                if (!tokenValidationResult.IsValid)
                {
                    return null;
                }

                // Extract user information from claims
                var userIdClaim = tokenValidationResult.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "oid");
                var emailClaim = tokenValidationResult.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == ClaimTypes.Upn);
                var tenantIdClaim = tokenValidationResult.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.TenantId || c.Type == "tid");
                var nameClaim = tokenValidationResult.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "name");

                if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
                {
                    return null;
                }

                return new AuthenticatedUser
                {
                    UserId = userIdClaim.Value,
                    Email = emailClaim?.Value ?? string.Empty,
                    TenantId = tenantIdClaim?.Value ?? string.Empty,
                    DisplayName = nameClaim?.Value ?? string.Empty,
                    Claims = tokenValidationResult.Claims?.ToList() ?? new List<Claim>()
                };
            }
            catch (Exception)
            {
                // In case of any exception during token validation
                return null;
            }
        }

        /// <summary>
        /// Validates JWT token and returns claims
        /// </summary>
        private Task<TokenValidationResult> ValidateTokenAsync(string token)
        {
            try
            {
                // NOTE: This is a minimal, signature-less parse of the JWT used to extract claims.
                // In a production scenario you should validate the token issuer, audience, and signature.
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);

                return Task.FromResult(new TokenValidationResult
                {
                    IsValid = true,
                    Claims = jwtToken.Claims
                });
            }
            catch (Exception)
            {
                return Task.FromResult(new TokenValidationResult
                {
                    IsValid = false,
                    Claims = null
                });
            }
        }
    }

    /// <summary>
    /// Represents an authenticated user extracted from the JWT token
    /// </summary>
    public class AuthenticatedUser
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<Claim> Claims { get; set; } = new List<Claim>();
    }

    /// <summary>
    /// Result of token validation operation
    /// </summary>
    public class TokenValidationResult
    {
        public bool IsValid { get; set; }
        public IEnumerable<Claim>? Claims { get; set; }
    }
}