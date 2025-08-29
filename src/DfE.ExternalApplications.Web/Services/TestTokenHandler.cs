using DfE.CoreLibs.Security.Configurations;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Services;

[ExcludeFromCodeCoverage]
public class TestTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TestAuthenticationOptions _options;
    private readonly ILogger<TestTokenHandler> _logger;

    private static class SessionKeys
    {
        public const string Token = "TestAuth:Token";
    }

    public TestTokenHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TestAuthenticationOptions> options,
        ILogger<TestTokenHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";
        
        _logger.LogDebug(">>>>>>>>>> Authentication >>> TestTokenHandler: Processing API request for user {UserId} to {RequestUri}", 
            userId, request.RequestUri);
        
        // Only modify requests if test authentication is enabled
        if (_options.Enabled && _httpContextAccessor.HttpContext is not null)
        {
            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestTokenHandler: Test authentication enabled, looking for test token for user {UserId}", userId);
            
            var testToken = _httpContextAccessor.HttpContext.Session.GetString(SessionKeys.Token);

            if (!string.IsNullOrEmpty(testToken))
            {
                // Replace the authorization header with the test token
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", testToken);
                
                _logger.LogDebug(">>>>>>>>>> Authentication >>> TestTokenHandler: Using test token for API request from user {UserId} to: {RequestUri}", 
                    userId, request.RequestUri);
            }
            else
            {
                _logger.LogWarning(">>>>>>>>>> Authentication >>> TestTokenHandler: No test token found in session for user {UserId} making API request to: {RequestUri}", 
                    userId, request.RequestUri);
            }
        }
        else
        {
            _logger.LogDebug(">>>>>>>>>> Authentication >>> TestTokenHandler: Test authentication disabled or no HttpContext for user {UserId}", userId);
        }

        var response = await base.SendAsync(request, cancellationToken);
        
        _logger.LogDebug(">>>>>>>>>> Authentication >>> TestTokenHandler: API request completed for user {UserId} to {RequestUri} with status {StatusCode}", 
            userId, request.RequestUri, response.StatusCode);
            
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(">>>>>>>>>> Authentication >>> TestTokenHandler: Authentication/Authorization failed for user {UserId} to {RequestUri}. Status: {StatusCode}", 
                userId, request.RequestUri, response.StatusCode);
        }

        return response;
    }
} 