using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security
{
    /// <summary>
    /// HTTP message handler that forwards specific headers from incoming requests to outgoing API calls.
    /// This is used to forward authentication-related headers (like Cypress test headers) from the web app to the API.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class HeaderForwardingHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<HeaderForwardingHandler> _logger;
        private readonly string[] _headersToForward;

        /// <summary>
        /// Default headers that should be forwarded from incoming requests to API calls if not configured
        /// </summary>
        private static readonly string[] DefaultHeadersToForward = new[]
        {
            "x-cypress-test",
            "x-cypress-secret"
        };

        /// <summary>
        /// Initializes a new instance of the HeaderForwardingHandler
        /// </summary>
        /// <param name="httpContextAccessor">Accessor to get the current HTTP context</param>
        /// <param name="apiSettings">API client settings containing configuration for headers to forward</param>
        /// <param name="logger">Logger for diagnostic information</param>
        public HeaderForwardingHandler(
            IHttpContextAccessor httpContextAccessor,
            ApiClientSettings apiSettings,
            ILogger<HeaderForwardingHandler> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            
            // Use configured headers if provided, otherwise use defaults
            _headersToForward = apiSettings.HeadersToForward?.Any() == true
                ? apiSettings.HeadersToForward
                : DefaultHeadersToForward;
        }

        /// <summary>
        /// Sends an HTTP request, forwarding configured headers from the incoming request
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext != null)
            {
                var headersForwarded = 0;

                // Forward each configured header if present in the incoming request
                foreach (var headerName in _headersToForward)
                {
                    if (httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
                    {
                        var value = headerValue.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            // Remove existing header if present (avoid duplicates)
                            if (request.Headers.Contains(headerName))
                            {
                                request.Headers.Remove(headerName);
                            }

                            // Add the forwarded header
                            request.Headers.Add(headerName, value);
                            headersForwarded++;

                            _logger.LogDebug(
                                "Forwarded header {HeaderName} to API request: {RequestUri}",
                                headerName,
                                request.RequestUri);
                        }
                    }
                }

                if (headersForwarded > 0)
                {
                    _logger.LogInformation(
                        "Forwarded {Count} header(s) to API request: {RequestUri}",
                        headersForwarded,
                        request.RequestUri);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}

